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

    static void UpdateQmodManifest(string path, ModMetadata metadata, EmbeddedAssetBundleOutput assets)
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
        if (root["fileCopies"] is JsonArray fileCopies && assets.Bundles.Count > 0)
        {
            var embeddedPaths = assets.Bundles.Select(bundle => bundle.PackagePath).ToHashSet(StringComparer.Ordinal);
            for (var index = fileCopies.Count - 1; index >= 0; index--)
            {
                if (fileCopies[index] is JsonObject fileCopy && embeddedPaths.Contains(fileCopy["name"]?.GetValue<string>() ?? ""))
                    fileCopies.RemoveAt(index);
            }
        }
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

    static void AddEmbeddedAssetBundleEmbedding(string cmakePath, EmbeddedAssetBundleOutput assets)
    {
        if (assets.Bundles.Count == 0)
            return;
        if (!File.Exists(cmakePath))
            throw new InvalidOperationException($"Quest template is missing CMakeLists.txt: {cmakePath}");

        var cmake = File.ReadAllText(cmakePath);
        const string marker = "# Transpiler embedded bundle objects";
        if (cmake.Contains(marker, StringComparison.Ordinal))
            return;

        File.AppendAllText(cmakePath, $$"""

        {{marker}}
        set(TRANSPILED_ASSET_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/assets")
        set(TRANSPILED_ASSET_OBJECT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/embeddedAssets")
        file(MAKE_DIRECTORY "${TRANSPILED_ASSET_OBJECT_DIRECTORY}")
        file(GLOB TRANSPILED_ASSET_FILES "${TRANSPILED_ASSET_DIRECTORY}/*")
        set(TRANSPILED_ASSET_OBJECTS)
        foreach(TRANSPILED_ASSET_FILE IN LISTS TRANSPILED_ASSET_FILES)
            get_filename_component(TRANSPILED_ASSET_NAME "${TRANSPILED_ASSET_FILE}" NAME)
            set(TRANSPILED_ASSET_OBJECT "${TRANSPILED_ASSET_OBJECT_DIRECTORY}/${TRANSPILED_ASSET_NAME}.o")
            add_custom_command(
                OUTPUT "${TRANSPILED_ASSET_OBJECT}"
                COMMAND ${CMAKE_OBJCOPY} "${TRANSPILED_ASSET_NAME}" "${TRANSPILED_ASSET_OBJECT}" --input-target binary --output-target elf64-aarch64
                DEPENDS "${TRANSPILED_ASSET_FILE}"
                WORKING_DIRECTORY "${TRANSPILED_ASSET_DIRECTORY}"
                VERBATIM
            )
            list(APPEND TRANSPILED_ASSET_OBJECTS "${TRANSPILED_ASSET_OBJECT}")
        endforeach()
        if(TRANSPILED_ASSET_OBJECTS)
            add_library(transpiled_embedded_assets OBJECT ${TRANSPILED_ASSET_OBJECTS})
            set_target_properties(transpiled_embedded_assets PROPERTIES LINKER_LANGUAGE CXX)
            target_link_libraries(${COMPILE_ID} PRIVATE transpiled_embedded_assets ${TRANSPILED_ASSET_OBJECTS})
        endif()
        """ + "\n");
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
