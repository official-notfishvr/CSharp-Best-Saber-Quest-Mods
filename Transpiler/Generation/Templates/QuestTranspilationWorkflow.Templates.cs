using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace Transpiler;

internal static partial class QuestTranspilationWorkflow
{
    static ModMetadata ReadModMetadata(AssemblyDefinition assembly)
    {
        var manifests = assembly.MainModule.Resources.OfType<EmbeddedResource>().Where(resource => resource.Name == "manifest.json" || resource.Name.EndsWith(".manifest.json", StringComparison.Ordinal)).ToArray();
        if (manifests.Length != 1)
            throw new InvalidOperationException($"PC assembly must contain exactly one embedded manifest.json or *.manifest.json resource; found {manifests.Length}.");
        var resource = manifests[0];
        using var stream = new MemoryStream(resource.GetResourceData());
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
        var name = root.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
        var version = root.TryGetProperty("version", out var versionValue) ? versionValue.GetString() : null;
        var author = root.TryGetProperty("author", out var authorValue) ? authorValue.GetString() : null;
        var description = root.TryGetProperty("description", out var descriptionValue) ? descriptionValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(description))
            throw new InvalidOperationException("Embedded manifest must include non-empty id, name, version, and description fields for Quest packaging.");
        return new ModMetadata(id, name, author ?? "", version, description);
    }

    static void UpdateProjectMetadata(string path, ModMetadata metadata)
    {
        if (!File.Exists(path))
            return;
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (root["info"] is JsonObject info)
        {
            info["name"] = metadata.Name;
            info["id"] = metadata.Id;
            info["version"] = metadata.Version;
            if (info["additionalData"] is JsonObject extra)
                extra["overrideSoName"] = BinaryName(metadata);
        }
        if (root["workspace"] is JsonObject workspace && workspace["qmodOutput"] is not null)
            workspace["qmodOutput"] = "./" + metadata.Name + ".qmod";
        if (root["project"] is JsonObject project)
            project["name"] = metadata.Name;
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var cmake = Path.Combine(Path.GetDirectoryName(path)!, "CMakeLists.txt");
        if (File.Exists(cmake))
            File.WriteAllText(cmake, Regex.Replace(File.ReadAllText(cmake), @"project\(\s*test\b", "project(" + SanitizeCppIdentifier(metadata.Id), RegexOptions.IgnoreCase));
    }

    static void UpdateQmodManifest(string path, ModMetadata metadata)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Quest template is missing mod.json: {path}");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["id"] = metadata.Id;
        root["name"] = metadata.Name;
        root["author"] = metadata.Author;
        root["version"] = metadata.Version;
        root["description"] = metadata.Description;
        root["modFiles"] = new JsonArray(JsonValue.Create(BinaryName(metadata)));
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    static string BinaryName(ModMetadata metadata) => "lib" + SanitizeCppIdentifier(metadata.Id) + ".so";

    static string SanitizeCppIdentifier(string value) => Regex.Replace(value, "[^A-Za-z0-9_]", "_");

    static void UpdateLoggerContext(string path, string modId)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Quest template is missing include/main.hpp: {path}");
        var source = File.ReadAllText(path);
        source = Regex.Replace(source, "Paper::ConstLoggerContext\\(\\\"[^\\\"]*\\\"\\)", "Paper::ConstLoggerContext(\"" + EscapeCpp(modId) + "\")");
        File.WriteAllText(path, source);
    }

    static void CopyTemplate(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var fileName in new[] { "CMakeLists.txt", "qpm.json", "qpm.shared.json", "mod.json", "mod.template.json", "extern.cmake" })
        {
            var file = Path.Combine(source, fileName);
            if (File.Exists(file))
                File.Copy(file, Path.Combine(destination, fileName), true);
        }
        foreach (var directoryName in new[] { "cmake", "scripts", "include" })
        {
            var directory = Path.Combine(source, directoryName);
            if (!Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
        }
        Directory.CreateDirectory(Path.Combine(destination, "src"));
    }

    static void SelectQuestDependencies(string qpmPath, string sharedPath, bool usesConfig, IReadOnlyDictionary<string, string> requiredQuestPackages)
    {
        var required = new HashSet<string>(StringComparer.Ordinal) { "beatsaber-hook", "scotland2", "bs-cordl", "paper2_scotland2", "bsml", "custom-types" };
        if (usesConfig)
            required.Add("config-utils");

        foreach (var path in new[] { qpmPath, sharedPath })
        {
            if (!File.Exists(path))
                continue;
            var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var dependencies = path == sharedPath ? (root["config"] as JsonObject)?["dependencies"] as JsonArray : root["dependencies"] as JsonArray;
            if (dependencies is null)
                continue;
            for (var index = dependencies.Count - 1; index >= 0; index--)
                if (dependencies[index]?["id"]?.GetValue<string>() is string id && !required.Contains(id))
                    dependencies.RemoveAt(index);
            foreach (var (id, versionRange) in requiredQuestPackages)
            {
                if (dependencies.Any(dependency => dependency?["id"]?.GetValue<string>() == id))
                    continue;
                dependencies.Add(
                    new JsonObject
                    {
                        ["id"] = id,
                        ["versionRange"] = versionRange,
                        ["additionalData"] = new JsonObject(),
                    }
                );
            }
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    static void AddBsmlIncludeDirectory(string cmakePath)
    {
        if (!File.Exists(cmakePath))
            throw new InvalidOperationException($"Quest template is missing CMakeLists.txt: {cmakePath}");
        var cmake = File.ReadAllText(cmakePath);
        const string marker = "# Transpiler BSML headers";
        if (!cmake.Contains(marker, StringComparison.Ordinal))
            File.AppendAllText(cmakePath, $"\n{marker}\ntarget_include_directories(${{COMPILE_ID}} PRIVATE ${{EXTERN_DIR}}/includes/bsml/shared)\n");
    }
}
