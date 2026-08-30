public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyBunit.Initialize();
        VerifyDiffPlex.Initialize(OutputType.Compact);
        VerifierSettings.SortPropertiesAlphabetically();
    }
}
