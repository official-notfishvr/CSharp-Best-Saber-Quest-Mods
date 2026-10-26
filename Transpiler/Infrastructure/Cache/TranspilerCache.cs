namespace Transpiler;

internal static class TranspilerCache
{
    public static string ComputeDiscoveryInputStamp(string pcGameRoot, string questHeaders, string pcGameVersion, IEnumerable<string> referencedAssemblyNames)
    {
        var managed = Path.Combine(pcGameRoot, "Beat Saber_Data", "Managed");
        var assemblyDirectories = new[] { managed, Path.Combine(pcGameRoot, "Plugins"), Path.Combine(pcGameRoot, "Libs") };
        var files = assemblyDirectories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            .Concat(Directory.EnumerateFiles(questHeaders, "*.hpp", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes("inventory-v12-unityw-pointer-return-match\n" + pcGameVersion + "\n" + string.Join(";", referencedAssemblyNames.Order(StringComparer.Ordinal)) + "\n"));
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes($"{file}|{info.Length}|{info.LastWriteTimeUtc.Ticks}\n"));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static void PersistQuestLibraryMappings(string output, string json)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "quest-library-mappings.json"), json);
    }
}
