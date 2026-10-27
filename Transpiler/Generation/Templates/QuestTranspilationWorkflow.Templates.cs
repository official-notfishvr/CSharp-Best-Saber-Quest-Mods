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

    static void WriteEmbeddedAssetBundles(string output, EmbeddedAssetBundleOutput assets)
    {
        var groups = assets.Bundles.GroupBy(bundle => bundle.PackagePath, StringComparer.Ordinal).ToArray();
        var collision = groups.FirstOrDefault(group => group.Select(bundle => bundle.ResourceName).Distinct(StringComparer.Ordinal).Count() > 1);
        if (collision is not null)
            throw new InvalidOperationException($"Embedded asset bundles collide at package path '{collision.Key}'. Rename the resource or adjust the transpiler's asset path mapping.");

        foreach (var bundle in groups.Select(group => group.First()))
        {
            var path = Path.Combine(output, bundle.PackagePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bundle.Data);
        }
    }

    static void AddEmbeddedAssetFileCopies(string manifestPath, EmbeddedAssetBundleOutput assets)
    {
        if (assets.Bundles.Count == 0 || !File.Exists(manifestPath))
            return;
        var root = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var copies = root["fileCopies"] as JsonArray ?? new JsonArray();
        foreach (var bundle in assets.Bundles.GroupBy(bundle => bundle.PackagePath, StringComparer.Ordinal).Select(group => group.First()))
        {
            if (copies.OfType<JsonObject>().Any(copy => copy["name"]?.GetValue<string>() == bundle.PackagePath))
                continue;
            copies.Add(new JsonObject { ["name"] = bundle.PackagePath, ["destination"] = bundle.DevicePath });
        }
        root["fileCopies"] = copies;
        File.WriteAllText(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    static void AddFileCopyPackaging(string output)
    {
        var qmodScript = Path.Combine(output, "scripts", "createqmod.ps1");
        if (!File.Exists(qmodScript))
            throw new InvalidOperationException($"Quest template is missing scripts/createqmod.ps1, required to package embedded asset bundles: {qmodScript}");
        var qmodSource = File.ReadAllText(qmodScript);
        const string marker = "# Transpiler embedded file copies";
        if (!qmodSource.Contains(marker, StringComparison.Ordinal))
        {
            const string insertionPoint = "$zip = $qmodName + \".zip\"";
            if (!qmodSource.Contains(insertionPoint, StringComparison.Ordinal))
                throw new InvalidOperationException("Quest template scripts/createqmod.ps1 has no supported QMOD file-list insertion point.");
            var addition =
                marker
                + "\n$packagedFileCopyRoots = @{}\nforeach ($fileCopy in $modJson.fileCopies) {\n    $relativePath = $fileCopy.name.Replace('\\', '/')\n    $path = \"./\" + $relativePath\n    if (-not (Test-Path $path)) {\n        Write-Output \"Error: could not find file copy source: $path\"\n        exit 1\n    }\n    $root = $relativePath.Split('/')[0]\n    $archiveSource = if ($relativePath.Contains('/')) { \"./\" + $root } else { $path }\n    if (-not $packagedFileCopyRoots.ContainsKey($archiveSource)) {\n        $packagedFileCopyRoots[$archiveSource] = $true\n        $filelist += ,$archiveSource\n    }\n}\n\n";
            qmodSource = qmodSource.Replace(insertionPoint, addition + insertionPoint, StringComparison.Ordinal);
            File.WriteAllText(qmodScript, qmodSource);
        }

        var copyScript = Path.Combine(output, "scripts", "copy.ps1");
        if (File.Exists(copyScript))
        {
            var source = File.ReadAllText(copyScript);
            const string copyMarker = "# Transpiler embedded file copies";
            if (!source.Contains(copyMarker, StringComparison.Ordinal))
            {
                const string insertionPoint = "& $PSScriptRoot/restart-game.ps1";
                if (!source.Contains(insertionPoint, StringComparison.Ordinal))
                    throw new InvalidOperationException("Quest template scripts/copy.ps1 has no supported asset-copy insertion point.");
                var addition =
                    copyMarker
                    + "\nforeach ($fileCopy in $modJson.fileCopies) {\n    $path = \"./\" + $fileCopy.name\n    if (Test-Path $path) {\n        $destinationDirectory = Split-Path -Parent $fileCopy.destination\n        & adb shell mkdir -p $destinationDirectory\n        & adb push $path $fileCopy.destination\n    }\n}\n\n";
                source = source.Replace(insertionPoint, addition + insertionPoint, StringComparison.Ordinal);
                File.WriteAllText(copyScript, source);
            }
        }
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
