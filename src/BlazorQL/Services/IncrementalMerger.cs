namespace BlazorQL;

/// <summary>
/// Accumulates a streamed execution into one response document. A plain result (a single response,
/// or a subscription event) replaces the accumulated document; an incremental-delivery payload
/// (@defer/@stream) merges into it. Ported from GraphiQL's <c>mergeIncrementalResult</c>, covering
/// both the older path-based format and the newer pending/completed id format.
/// </summary>
public sealed class IncrementalMerger
{
    JsonObject? result;

    // Incremental-delivery ids registered by "pending" entries → the path they deliver to.
    readonly Dictionary<string, List<object>> pendingPaths = [];

    public bool HasResult => result is not null;

    /// <summary>The accumulated response, serialized.</summary>
    public string Render() =>
        result?.ToJsonString(RenderOptions) ?? "";

    static readonly JsonSerializerOptions RenderOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void Add(JsonElement payload)
    {
        var node = JsonNode.Parse(payload.GetRawText())!.AsObject();
        if (result is null || !IsIncremental(node))
        {
            // A fresh document: the single result, a subscription event, or the opening payload of
            // an incremental stream (whose pending entries still need registering).
            result = node;
            pendingPaths.Clear();
            RegisterPending(node);
            return;
        }

        Merge(node);
    }

    static bool IsIncremental(JsonObject node) =>
        node.ContainsKey("hasNext") ||
        node.ContainsKey("incremental") ||
        node.ContainsKey("pending") ||
        node.ContainsKey("completed") ||
        node.ContainsKey("items") ||
        node.ContainsKey("path");

    void Merge(JsonObject incremental)
    {
        RegisterPending(incremental);

        // Protocol bookkeeping, not data: tracked here, not rendered as part of the document.
        if (incremental["hasNext"] is JsonNode hasNext)
        {
            result!["hasNext"] = hasNext.DeepClone();
        }

        var items = incremental["items"]?.AsArray();
        if (items is not null)
        {
            var id = incremental["id"]?.GetValue<string>();
            if (id is not null)
            {
                if (!pendingPaths.TryGetValue(id, out var path))
                {
                    throw new InvalidOperationException("Invalid incremental delivery format.");
                }

                var list = (JsonArray) GetValue(path)!;
                foreach (var entry in items.ToList())
                {
                    var item = entry!;
                    item.Parent?.AsArray().Remove(item);
                    list.Add(item);
                }
            }
            else
            {
                var path = PathOf(incremental);
                var index = (int) path[^1];
                foreach (var entry in items.ToList())
                {
                    var item = entry!;
                    item.Parent?.AsArray().Remove(item);
                    path[^1] = index++;
                    SetValue(path, item, merge: false);
                }
            }
        }

        var data = incremental["data"];
        if (data is not null)
        {
            List<object> path;
            var id = incremental["id"]?.GetValue<string>();
            if (id is not null)
            {
                if (!pendingPaths.TryGetValue(id, out var pending))
                {
                    throw new InvalidOperationException("Invalid incremental delivery format.");
                }

                path = [.. pending];
                if (incremental["subPath"] is JsonArray subPath)
                {
                    path.AddRange(subPath.Select(Segment));
                }
            }
            else
            {
                path = PathOf(incremental);
            }

            data.Parent?.AsObject().Remove("data");
            SetValue(path, data, merge: true);
        }

        if (incremental["errors"] is JsonArray errors)
        {
            var target = result!["errors"] as JsonArray;
            if (target is null)
            {
                result!["errors"] = target = [];
            }

            foreach (var error in errors.ToList())
            {
                errors.Remove(error);
                target.Add(error);
            }
        }

        if (incremental["extensions"] is JsonNode extensions)
        {
            incremental.Remove("extensions");
            SetValue(["extensions"], extensions, merge: true);
        }

        if (incremental["incremental"] is JsonArray nested)
        {
            foreach (var entry in nested.ToList())
            {
                nested.Remove(entry);
                Merge(entry!.AsObject());
            }
        }

        if (incremental["completed"] is JsonArray completed)
        {
            foreach (var entry in completed)
            {
                var completedId = entry!["id"]!.GetValue<string>();
                pendingPaths.Remove(completedId);
                if (entry["errors"] is JsonArray completedErrors)
                {
                    var target = result!["errors"] as JsonArray;
                    if (target is null)
                    {
                        result!["errors"] = target = [];
                    }

                    foreach (var error in completedErrors.ToList())
                    {
                        completedErrors.Remove(error);
                        target.Add(error);
                    }
                }
            }
        }

        // Once every deferred piece has landed, the protocol fields say nothing a reader wants.
        if (pendingPaths.Count == 0 &&
            result!["hasNext"]?.GetValue<bool>() == false)
        {
            result.Remove("hasNext");
            result.Remove("pending");
        }
    }

    void RegisterPending(JsonObject node)
    {
        if (node["pending"] is not JsonArray pending)
        {
            return;
        }

        foreach (var entry in pending)
        {
            var id = entry!["id"]!.GetValue<string>();
            List<object> path = ["data"];
            path.AddRange(entry["path"]!.AsArray().Select(Segment));
            pendingPaths[id] = path;
        }
    }

    static List<object> PathOf(JsonObject incremental)
    {
        List<object> path = ["data"];
        if (incremental["path"] is JsonArray segments)
        {
            path.AddRange(segments.Select(Segment));
        }

        return path;
    }

    static object Segment(JsonNode? node) =>
        node!.GetValueKind() == JsonValueKind.Number ? node.GetValue<int>() : node.GetValue<string>();

    JsonNode? GetValue(List<object> path)
    {
        JsonNode? current = result;
        foreach (var segment in path)
        {
            current = segment is int index ? current?[index] : current?[(string) segment];
        }

        return current;
    }

    void SetValue(List<object> path, JsonNode value, bool merge)
    {
        JsonNode current = result!;
        for (var i = 0; i < path.Count - 1; i++)
        {
            var segment = path[i];
            var next = segment is int index ? current[index] : current[(string) segment];
            if (next is null)
            {
                // The next segment's kind decides whether the container is an array or an object.
                next = path[i + 1] is int ? new JsonArray() : new JsonObject();
                Assign(current, segment, next);
            }

            current = next;
        }

        var last = path[^1];
        if (merge &&
            value is JsonObject source &&
            (last is int li ? current[li] : current[(string) last]) is JsonObject target)
        {
            DeepMerge(target, source);
            return;
        }

        Assign(current, last, value);
    }

    static void Assign(JsonNode container, object segment, JsonNode value)
    {
        if (segment is int index)
        {
            var array = container.AsArray();
            while (array.Count <= index)
            {
                array.Add(null);
            }

            array[index] = value;
        }
        else
        {
            container[(string) segment] = value;
        }
    }

    static void DeepMerge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source.ToList())
        {
            source.Remove(key);
            if (value is JsonObject sourceObject &&
                target[key] is JsonObject targetObject)
            {
                DeepMerge(targetObject, sourceObject);
            }
            else
            {
                target[key] = value;
            }
        }
    }
}
