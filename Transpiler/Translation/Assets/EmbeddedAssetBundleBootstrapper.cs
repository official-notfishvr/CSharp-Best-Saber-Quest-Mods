using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed record PackagedEmbeddedAssetBundle(string ResourceName, string PackagePath, string DevicePath, byte[] Data, string AssetName, FieldDefinition TargetField);

internal sealed record EmbeddedAssetBundleOutput(IReadOnlyList<PackagedEmbeddedAssetBundle> Bundles, IReadOnlyList<string> Includes, IReadOnlyList<string> SupportCode, IReadOnlyList<string> StartupLines);

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
                if (
                    instructions[index].Operand is not GenericInstanceMethod genericMethod
                    || genericMethod.Name != "LoadAssetAsync"
                    || genericMethod.DeclaringType.FullName != "UnityEngine.AssetBundle"
                    || genericMethod.GenericArguments.Count != 1
                    || genericMethod.GenericArguments[0].FullName != "UnityEngine.Material"
                )
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
                discovered[(resourceName, assetLoad.AssetName, field.FullName)] = new(resourceName, packagePath, devicePath, resource.GetResourceData(), assetLoad.AssetName, field);
            }
        }

        var bundles = discovered.Values.OrderBy(bundle => bundle.ResourceName, StringComparer.Ordinal).ThenBy(bundle => bundle.AssetName, StringComparer.Ordinal).ToArray();
        var includes = bundles.Length == 0 ? Array.Empty<string>() : new[] { "UnityEngine/AssetBundle.hpp", "UnityEngine/Material.hpp" };
        var support = bundles.Select(bundle => $"extern ::UnityEngine::Material* {IlTranslationContext.StaticFieldName(bundle.TargetField)};").Distinct(StringComparer.Ordinal).ToArray();
        var startup = new List<string>();
        foreach (var (bundle, index) in bundles.Select((bundle, index) => (bundle, index)))
        {
            var variable = "il_embedded_bundle_" + index;
            var target = IlTranslationContext.StaticFieldName(bundle.TargetField);
            startup.Add($"if ({target} == nullptr) {{");
            startup.Add($"    auto {variable} = ::UnityEngine::AssetBundle::LoadFromFile(StringW(\"{EscapeCpp(bundle.DevicePath)}\"));");
            startup.Add($"    if ({variable} != nullptr) {{");
            startup.Add($"        {target} = {variable}->LoadAsset<::UnityEngine::Material*>(StringW(\"{EscapeCpp(bundle.AssetName)}\"));");
            startup.Add($"        {variable}->Unload(false);");
            startup.Add("    }");
            startup.Add("}");
        }
        return new(bundles, includes, support, startup);
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

    private static string EscapeCpp(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
