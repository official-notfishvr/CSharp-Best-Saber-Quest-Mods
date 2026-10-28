using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed record PackagedEmbeddedAssetBundle(string ResourceName, string PackagePath, string DevicePath, byte[] Data, string AssetName, FieldDefinition TargetField, string SymbolName);

internal sealed record EmbeddedAssetBundleOutput(
    IReadOnlyList<PackagedEmbeddedAssetBundle> Bundles,
    IReadOnlyList<string> Includes,
    IReadOnlyList<string> SupportCode,
    IReadOnlyList<string> StartupLines,
    IReadOnlyList<string> HookCode,
    IReadOnlyList<string> HookNames
);

internal static class EmbeddedAssetBundleBootstrapper
{
    public static EmbeddedAssetBundleOutput Discover(AssemblyDefinition assembly, string modId)
    {
        var resources = assembly.MainModule.Resources.OfType<EmbeddedResource>().ToDictionary(resource => resource.Name, StringComparer.Ordinal);
        var discovered = new Dictionary<(string Resource, string Asset, string Field), PackagedEmbeddedAssetBundle>();

        foreach (var method in CecilTypeWalker.Flatten(assembly.MainModule.Types).SelectMany(type => type.Methods).Where(method => method.HasBody))
        {
            var instructions = method.Body.Instructions;
            var embeddedNames = instructions.Where(instruction => instruction.OpCode.Code == Code.Ldstr && instruction.Operand is string).Select(instruction => (string)instruction.Operand).Where(resources.ContainsKey).Distinct(StringComparer.Ordinal).ToArray();
            if (embeddedNames.Length == 0)
                continue;

            var materialFields = instructions
                .Where(instruction => instruction.OpCode.Code == Code.Stsfld && instruction.Operand is FieldReference)
                .Select(instruction => ((FieldReference)instruction.Operand).Resolve())
                .Where(field => field is { IsStatic: true } && field.FieldType.FullName == "UnityEngine.Material")
                .Cast<FieldDefinition>()
                .DistinctBy(field => field.FullName)
                .ToArray();
            if (materialFields.Length == 0)
                continue;

            var materialLoads = new List<(int Index, string AssetName)>();
            for (var index = 0; index < instructions.Count; index++)
            {
                if (instructions[index].Operand is not GenericInstanceMethod genericMethod || genericMethod.Name != "LoadAssetAsync" || genericMethod.DeclaringType.FullName != "UnityEngine.AssetBundle" || genericMethod.GenericArguments.Count != 1 || genericMethod.GenericArguments[0].FullName != "UnityEngine.Material")
                    continue;

                var assetInstruction = PreviousMeaningfulInstruction(instructions, index - 1);
                if (assetInstruction?.OpCode.Code == Code.Ldstr && assetInstruction.Operand is string assetName)
                    materialLoads.Add((index, assetName));
            }
            if (materialLoads.Count == 0)
                continue;

            foreach (var resourceName in embeddedNames)
            foreach (var assetLoad in materialLoads)
            foreach (var field in materialFields)
            {
                var resource = resources[resourceName];
                var fileName = ResourceFileName(resourceName);
                var packagePath = "assets/" + fileName;
                var devicePath = "/sdcard/ModData/com.beatgames.beatsaber/Mods/" + modId + "/" + packagePath;
                var symbol = SanitizeSymbol(fileName);
                discovered[(resourceName, assetLoad.AssetName, field.FullName)] = new(resourceName, packagePath, devicePath, resource.GetResourceData(), assetLoad.AssetName, field, symbol);
            }
        }

        var bundles = discovered.Values.OrderBy(bundle => bundle.ResourceName, StringComparer.Ordinal).ThenBy(bundle => bundle.AssetName, StringComparer.Ordinal).ToArray();
        var symbolCollision = bundles.GroupBy(bundle => bundle.SymbolName, StringComparer.Ordinal).FirstOrDefault(group => group.Select(bundle => bundle.ResourceName).Distinct(StringComparer.Ordinal).Count() > 1);
        if (symbolCollision is not null)
            throw new InvalidOperationException($"Embedded asset resources collide at native symbol '{symbolCollision.Key}'. Rename the resource so its asset filename is unique.");

        var includes = bundles.Length == 0
            ? Array.Empty<string>()
            : new[]
            {
                "GlobalNamespace/QuestAppInit.hpp",
                "UnityEngine/AssetBundle.hpp",
                "UnityEngine/AssetBundleCreateRequest.hpp",
                "UnityEngine/AssetBundleRequest.hpp",
                "UnityEngine/AsyncOperation.hpp",
                "UnityEngine/Material.hpp",
                "custom-types/shared/coroutine.hpp",
                "lapiz/shared/zenject/Zenjector.hpp",
            };
        var support = new List<string>();
        if (bundles.Length > 0)
        {
            support.Add("#include <algorithm>");
            support.Add("static ::GlobalNamespace::QuestAppInit* il_embedded_active_app_init = nullptr;");
            support.AddRange(bundles.Select(bundle => $"extern ::UnityEngine::Material* {IlTranslationContext.StaticFieldName(bundle.TargetField)};").Distinct(StringComparer.Ordinal));
            foreach (var (bundle, index) in bundles.Select((bundle, index) => (bundle, index)))
                support.AddRange(GenerateBundleCoroutine(bundle, index));
        }

        var startup = bundles.Select((bundle, index) =>
            "::Lapiz::Zenject::Zenjector::Get(modInfo)->Install(::Lapiz::Zenject::Location::App, [](::Zenject::DiContainer*) {\n"
            + string.Join("\n", GenerateLoadLines(bundle, index).Select(line => "    " + line))
            + "\n});").ToArray();
        var hookCode = bundles.Length == 0
            ? Array.Empty<string>()
            : new[]
            {
                "MAKE_HOOK_MATCH(il_embedded_QuestAppInit_InstallBindings, &::GlobalNamespace::QuestAppInit::InstallBindings, void, ::GlobalNamespace::QuestAppInit* self) {\n"
                + "    auto* previous = il_embedded_active_app_init;\n"
                + "    il_embedded_active_app_init = self;\n"
                + "    il_embedded_QuestAppInit_InstallBindings(self);\n"
                + "    il_embedded_active_app_init = previous;\n"
                + "}"
            };
        return new(bundles, includes, support, startup, hookCode, bundles.Length == 0 ? Array.Empty<string>() : new[] { "il_embedded_QuestAppInit_InstallBindings" });
    }

    internal static IReadOnlyList<string> GenerateLoadLines(PackagedEmbeddedAssetBundle bundle, int index)
    {
        var target = IlTranslationContext.StaticFieldName(bundle.TargetField);
        return new[]
        {
            $"if ({target} == nullptr && il_embedded_active_app_init != nullptr)",
            $"    il_embedded_active_app_init->StartCoroutine(custom_types::Helpers::CoroutineHelper::New(il_load_embedded_bundle_{index}()));",
        };
    }

    private static IReadOnlyList<string> GenerateBundleCoroutine(PackagedEmbeddedAssetBundle bundle, int index)
    {
        var target = IlTranslationContext.StaticFieldName(bundle.TargetField);
        return new[]
        {
            $"extern \"C\" uint8_t _binary_{bundle.SymbolName}_start[];",
            $"extern \"C\" uint8_t _binary_{bundle.SymbolName}_end[];",
            $"static custom_types::Helpers::Coroutine il_load_embedded_bundle_{index}() {{",
            $"    if ({target} != nullptr) co_return;",
            $"    auto* begin = _binary_{bundle.SymbolName}_start;",
            $"    auto* end = _binary_{bundle.SymbolName}_end;",
            "    ArrayW<uint8_t> bytes(static_cast<size_t>(end - begin));",
            "    std::copy(begin, end, bytes.begin());",
            "    using AssetBundleLoadFromMemoryAsync = function_ptr_t<::UnityEngine::AssetBundleCreateRequest*, ArrayW<uint8_t>, int>;",
            "    static auto loadFromMemoryAsync = reinterpret_cast<AssetBundleLoadFromMemoryAsync>(il2cpp_functions::resolve_icall(\"UnityEngine.AssetBundle::LoadFromMemoryAsync_Internal\"));",
            "    if (loadFromMemoryAsync == nullptr) co_return;",
            "    auto request = loadFromMemoryAsync(bytes, 0);",
            "    if (request == nullptr) co_return;",
            "    request->set_allowSceneActivation(true);",
            "    co_yield reinterpret_cast<::System::Collections::IEnumerator*>(request);",
            "    auto assetBundle = request->get_assetBundle();",
            "    if (assetBundle == nullptr) co_return;",
            $"    auto materialRequest = assetBundle->LoadAssetAsync<::UnityEngine::Material*>(StringW(\"{EscapeCpp(bundle.AssetName)}\"));",
            "    if (materialRequest != nullptr) {",
            "        co_yield reinterpret_cast<::System::Collections::IEnumerator*>(materialRequest);",
            $"        {target} = materialRequest->get_asset().cast<::UnityEngine::Material>().unsafePtr();",
            "    }",
            "    assetBundle->UnloadAsync(false);",
            "    co_return;",
            "}",
        };
    }

    private static Instruction? PreviousMeaningfulInstruction(Mono.Collections.Generic.Collection<Instruction> instructions, int index)
    {
        while (index >= 0 && instructions[index].OpCode.Code == Code.Nop)
            index--;
        return index >= 0 ? instructions[index] : null;
    }

    private static string ResourceFileName(string resourceName)
    {
        var separator = resourceName.LastIndexOf(".AssetBundles.", StringComparison.Ordinal);
        var name = separator >= 0 ? resourceName[(separator + ".AssetBundles.".Length)..] : resourceName[(resourceName.LastIndexOf('.') + 1)..];
        name = System.Text.RegularExpressions.Regex.Replace(name, "[^A-Za-z0-9_.-]", "_");
        return name.Length == 0 ? "embedded.bundle" : name;
    }

    private static string SanitizeSymbol(string value) => System.Text.RegularExpressions.Regex.Replace(value, "[^A-Za-z0-9_]", "_");

    private static string EscapeCpp(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
