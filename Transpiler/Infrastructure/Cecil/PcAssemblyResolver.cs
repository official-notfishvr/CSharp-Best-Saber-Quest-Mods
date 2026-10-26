using Mono.Cecil;

namespace Transpiler;

internal static class PcAssemblyResolver
{
    public static DefaultAssemblyResolver Create(string pcManaged, string assemblyPath)
    {
        var resolver = new DefaultAssemblyResolver();
        var gameData = Directory.GetParent(pcManaged)?.FullName;
        var gameRoot = gameData is null ? null : Directory.GetParent(gameData)?.FullName;
        var modDirectory = Path.GetDirectoryName(assemblyPath);
        var modRoot = modDirectory is null ? null : Directory.GetParent(modDirectory)?.FullName;
        foreach (
            var directory in new[]
            {
                pcManaged,
                gameRoot is null ? null : Path.Combine(gameRoot, "Plugins"),
                gameRoot is null ? null : Path.Combine(gameRoot, "Libs"),
                gameRoot is null ? null : Path.Combine(gameRoot, "IPA", "Libs"),
                modDirectory,
                modRoot is null ? null : Path.Combine(modRoot, "Plugins"),
                modRoot is null ? null : Path.Combine(modRoot, "Libs"),
            }
        )
            if (directory is not null && Directory.Exists(directory))
                resolver.AddSearchDirectory(directory);
        return resolver;
    }
}
