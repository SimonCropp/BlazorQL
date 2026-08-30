# Deploying to GitHub Pages

A standalone Blazor WebAssembly publish is a static site, and BlazorQL keeps every asset reference — the BlazorMonaco editor assets, the host module, the local schema — base-href relative, so the whole IDE runs from static hosting under a repository sub-path.

The workflow (`.github/workflows/pages.yml`) publishes the sample and adjusts three things for Pages:

<!-- snippet: pagesRewrite -->
<a id='snippet-pagesRewrite'></a>
```yml
# GitHub Pages serves the site under /<repo>/, so the app's base href moves with it; the
# underscore-prefixed _framework directory needs Jekyll turned off; and a 404 that re-serves
# the app keeps deep links working on a static host.
- name: Prepare for Pages
  run: |
    sed -i 's|<base href="/" />|<base href="/BlazorQL/" />|' publish/wwwroot/index.html
    touch publish/wwwroot/.nojekyll
    cp publish/wwwroot/index.html publish/wwwroot/404.html
```
<sup><a href='/.github/workflows/pages.yml#L26-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-pagesRewrite' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Then `actions/upload-pages-artifact` + `actions/deploy-pages` publish `publish/wwwroot`.

The sub-path claim is not taken on faith: the browser test suite serves the same published output mounted under `/BlazorQL/` on every run, with a zero-console-error assertion — the exact failure mode a broken worker url or absolute asset path produces.

For a different repository, change the base href in the rewrite step to `/<repo>/`.
