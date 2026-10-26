using System.Text.Json;
using Mono.Cecil;

namespace Transpiler;

internal static class QuestTargetProfileValidator
{
    public static void ValidatePcAssemblies(CompatibilityProfile profile, string pcManagedRoot)
    {
        foreach (var expected in profile.PcAssemblies)
        {
            var path = FindPcAssembly(pcManagedRoot, expected.Name);
            if (path is null)
                throw new InvalidOperationException($"PC compatibility profile '{profile.Id}' requires {expected.Name}.dll in the selected game's Managed, Plugins, or Libs directory.");
            using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = false });
            if (assembly.MainModule.Mvid != expected.Mvid)
                throw new InvalidOperationException($"PC API assembly mismatch for profile '{profile.Id}': {expected.Name}.dll MVID is {assembly.MainModule.Mvid}, expected {expected.Mvid}. Select the matching Beat Saber install or add a reviewed compatibility profile.");
        }
    }

    public static string? FindPcAssembly(string pcManagedRoot, string assemblyName)
    {
        var gameData = Directory.GetParent(pcManagedRoot)?.FullName;
        var gameRoot = gameData is null ? null : Directory.GetParent(gameData)?.FullName;
        var candidates = new[] { Path.Combine(pcManagedRoot, assemblyName + ".dll"), gameRoot is null ? "" : Path.Combine(gameRoot, "Plugins", assemblyName + ".dll"), gameRoot is null ? "" : Path.Combine(gameRoot, "Libs", assemblyName + ".dll") };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static void Validate(CompatibilityProfile profile, string templateRoot)
    {
        var manifestPath = Path.Combine(templateRoot, "mod.template.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"Quest template is missing mod.template.json: {manifestPath}");
        using (var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
        {
            var root = manifest.RootElement;
            var packageId = root.GetProperty("packageId").GetString();
            var packageVersion = root.GetProperty("packageVersion").GetString();
            if (packageId != profile.QuestPackageId || packageVersion != profile.QuestPackageVersion)
                throw new InvalidOperationException($"Quest template targets {packageId}@{packageVersion}, but compatibility profile '{profile.Id}' requires {profile.QuestPackageId}@{profile.QuestPackageVersion}.");
        }

        var lockPath = Path.Combine(templateRoot, "qpm.shared.json");
        if (!File.Exists(lockPath))
            throw new InvalidOperationException($"Quest template is missing QPM lock data: {lockPath}");
        using var lockDocument = JsonDocument.Parse(File.ReadAllText(lockPath));
        if (!lockDocument.RootElement.TryGetProperty("restoredDependencies", out var dependencies))
            throw new InvalidOperationException($"Quest template QPM lock has no restoredDependencies; restore its SDK before using compatibility profile '{profile.Id}'.");
        ValidateLockedDependency(dependencies, profile.QuestApiPackage, profile.QuestApiVersion, profile.Id);
    }

    private static void ValidateLockedDependency(JsonElement dependencies, string package, string version, string profileId)
    {
        var matches = dependencies.EnumerateArray().Where(d => d.TryGetProperty("dependency", out var dependency) && dependency.TryGetProperty("id", out var id) && id.GetString() == package).ToList();
        if (matches.Count != 1 || matches[0].GetProperty("version").GetString() != version)
            throw new InvalidOperationException($"Quest template must lock {package} exactly at {version} for compatibility profile '{profileId}'.");
    }
}
