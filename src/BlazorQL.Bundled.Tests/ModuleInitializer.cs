// One browser drives every test, and the screenshot baselines and timeouts were set against a
// serial run, which is what NUnit gave by default.
[assembly: NotInParallel]

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        // Downloads the Chromium build on first run so the UI tests work on a clean machine / CI.
        VerifyPlaywright.Initialize(installPlaywright: true);
        VerifierSettings.UseSsimForPng();
    }
}
