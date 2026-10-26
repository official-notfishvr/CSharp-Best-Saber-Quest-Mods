namespace Transpiler;

internal static class QpmCache
{
    public static string Root
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("QPM_CACHE");
            return !string.IsNullOrWhiteSpace(configured) ? Path.GetFullPath(configured) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QPM-RS", "cache");
        }
    }
}
