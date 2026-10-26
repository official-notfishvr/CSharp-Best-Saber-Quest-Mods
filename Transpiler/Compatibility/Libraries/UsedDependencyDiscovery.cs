using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace Transpiler;

internal static partial class QuestLibraryMappingGenerator
{
    private static IReadOnlyList<PcMethodSignature> ReadExternalCalls(AssemblyDefinition sourceMod, IReadOnlySet<string> referencedAssemblies) =>
        CecilTypeWalker
            .Flatten(sourceMod.MainModule.Types)
            .SelectMany(type => type.Methods.Where(method => method.HasBody))
            .SelectMany(method => method.Body.Instructions)
            .Where(instruction => instruction.OpCode.Code is Mono.Cecil.Cil.Code.Call or Mono.Cecil.Cil.Code.Callvirt or Mono.Cecil.Cil.Code.Newobj)
            .Select(instruction => instruction.Operand as MethodReference)
            .Where(method => method is not null)
            .Select(method => PcMethodSignature.FromReference(method!, GetScopeAssemblyName(method!.DeclaringType)))
            .Where(method => referencedAssemblies.Contains(method.PcAssembly))
            .DistinctBy(method => method.Key)
            .ToArray();

    private static IReadOnlyList<StructuralAssociation> DiscoverStructuralAssociations(AssemblyDefinition sourceMod, IReadOnlyList<PcMethodSignature> sourceCalls, IReadOnlyList<QpmPackage> packages, string candidatesPath)
    {
        var usedCallKeys = sourceCalls.Select(call => call.Key).ToHashSet(StringComparer.Ordinal);
        var harmonyTargets = new HashSet<(string Assembly, string Type, string Method)>();
        foreach (var type in CecilTypeWalker.Flatten(sourceMod.MainModule.Types))
        foreach (var attribute in type.CustomAttributes.Concat(type.Methods.SelectMany(method => method.CustomAttributes)))
        {
            if (!attribute.AttributeType.Namespace.StartsWith("HarmonyLib", StringComparison.Ordinal) || !attribute.AttributeType.Name.Contains("HarmonyPatch", StringComparison.Ordinal))
                continue;
            var targetType = attribute.ConstructorArguments.Select(argument => argument.Value).OfType<TypeReference>().FirstOrDefault();
            var targetMethod = attribute.ConstructorArguments.Select(argument => argument.Value).OfType<string>().FirstOrDefault();
            if (targetType is not null && !string.IsNullOrWhiteSpace(targetMethod))
                harmonyTargets.Add((GetScopeAssemblyName(targetType), targetType.FullName, targetMethod!));
        }

        using var document = JsonDocument.Parse(File.ReadAllText(candidatesPath));
        if (!document.RootElement.TryGetProperty("discovery", out var discovery) || !discovery.TryGetProperty("exactMethodCandidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            return Array.Empty<StructuralAssociation>();

        var associations = new List<StructuralAssociation>();
        foreach (var candidate in candidates.EnumerateArray())
        {
            var assembly = candidate.GetProperty("pcAssembly").GetString()!;
            var pcType = candidate.GetProperty("pcType").GetString()!;
            var pcMethod = candidate.GetProperty("pcMethod").GetString()!;
            var methodKey = CandidateMethodKey(candidate);
            var isUsed = usedCallKeys.Contains(methodKey) || harmonyTargets.Contains((assembly, pcType, pcMethod));
            if (!isUsed || candidate.GetProperty("resolution").GetString() != "unique-structural-match" || candidate.GetProperty("matchingQuestDeclarations").GetInt32() != 1)
                continue;

            var publicHeader = candidate.GetProperty("publicHeader").GetString()!;
            var declarationHeader = candidate.GetProperty("declarationHeader").GetString()!;
            var owningPackages = packages.Where(package => PackageContainsHeader(package, publicHeader) && PackageContainsHeader(package, declarationHeader)).ToArray();
            if (owningPackages.Length != 1)
                continue;
            var package = owningPackages[0];
            associations.Add(new StructuralAssociation(assembly, package, methodKey, pcType, pcMethod, candidate.GetProperty("questType").GetString()!, candidate.GetProperty("questMethod").GetString()!, package.Id + "/" + publicHeader, candidate.GetProperty("resolution").GetString()!));
        }
        return associations;
    }

    private static IReadOnlyList<StructuralTypeAssociation> DiscoverUsedTypeAssociations(IReadOnlyList<PcMethodSignature> sourceCalls, IReadOnlyList<string> referencedAssemblies, IReadOnlyList<QpmPackage> packages, string candidatesPath)
    {
        var referenced = referencedAssemblies.ToHashSet(StringComparer.Ordinal);
        var usedTypes = sourceCalls
            .SelectMany(call => new[] { call.ReturnType }.Concat(call.Parameters))
            .SelectMany(FlattenTypeReferences)
            .Where(type => referenced.Contains(GetScopeAssemblyName(type)))
            .Select(type => (Assembly: GetScopeAssemblyName(type), Type: type.FullName))
            .Distinct()
            .ToHashSet();

        using var document = JsonDocument.Parse(File.ReadAllText(candidatesPath));
        if (!document.RootElement.TryGetProperty("discovery", out var discovery) || !discovery.TryGetProperty("exactMethodCandidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            return Array.Empty<StructuralTypeAssociation>();

        var matched = new List<(string Assembly, string Type, QpmPackage Package, string QuestType, string Header)>();
        foreach (var candidate in candidates.EnumerateArray())
        {
            var assembly = candidate.GetProperty("pcAssembly").GetString()!;
            var pcType = candidate.GetProperty("pcType").GetString()!;
            if (!usedTypes.Contains((assembly, pcType)) || candidate.GetProperty("resolution").GetString() != "unique-structural-match" || candidate.GetProperty("matchingQuestDeclarations").GetInt32() != 1)
                continue;
            var publicHeader = candidate.GetProperty("publicHeader").GetString()!;
            var declarationHeader = candidate.GetProperty("declarationHeader").GetString()!;
            var owningPackages = packages.Where(package => PackageContainsHeader(package, publicHeader) && PackageContainsHeader(package, declarationHeader)).ToArray();
            if (owningPackages.Length != 1)
                continue;
            matched.Add((assembly, pcType, owningPackages[0], candidate.GetProperty("questType").GetString()!, publicHeader));
        }

        return matched
            .GroupBy(item => (item.Assembly, item.Type, item.Package.Id))
            .Select(group => new StructuralTypeAssociation(
                group.Key.Assembly,
                group.Key.Type,
                group.First().Package,
                group.Select(item => item.QuestType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                group.Select(item => item.Header).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                group.Count()
            ))
            .ToArray();
    }

    private static IEnumerable<TypeReference> FlattenTypeReferences(TypeReference type)
    {
        if (type is TypeSpecification specification)
        {
            foreach (var nested in FlattenTypeReferences(specification.ElementType))
                yield return nested;
            if (type is GenericInstanceType generic)
                foreach (var argument in generic.GenericArguments.SelectMany(FlattenTypeReferences))
                    yield return argument;
            yield break;
        }
        if (type is GenericParameter)
            yield break;
        yield return type;
    }

    private static JsonArray DiscoverFrameworkAdapters(AssemblyDefinition sourceMod, IReadOnlyList<PcMethodSignature> sourceCalls)
    {
        var adapters = new JsonArray();
        var allAttributes = CecilTypeWalker.Flatten(sourceMod.MainModule.Types).SelectMany(type => type.CustomAttributes.Concat(type.Methods.SelectMany(method => method.CustomAttributes))).ToArray();
        var harmonyPatches = allAttributes.Where(attribute => attribute.AttributeType.Namespace.StartsWith("HarmonyLib", StringComparison.Ordinal) && attribute.AttributeType.Name.Contains("HarmonyPatch", StringComparison.Ordinal)).ToArray();
        if (harmonyPatches.Length > 0)
        {
            var calls = sourceCalls.Where(call => call.PcType.StartsWith("HarmonyLib.", StringComparison.Ordinal)).ToArray();
            adapters.Add(
                new JsonObject
                {
                    ["pcNamespace"] = "HarmonyLib",
                    ["questPackageId"] = "beatsaber-hook",
                    ["mechanism"] = "Harmony patch metadata is lowered to native Quest hooks",
                    ["status"] = "handled_by_transpiler_adapter",
                    ["evidence"] = new JsonObject
                    {
                        ["patchMetadataAttributeCount"] = harmonyPatches.Length,
                        ["referencedMethods"] = Strings(calls.Select(call => call.PcMethod).Distinct(StringComparer.Ordinal)),
                        ["hookInstallPath"] = "Harmony target discovery -> MAKE_HOOK_MATCH -> INSTALL_HOOK",
                    },
                    ["untranslatedCalls"] = Strings(calls.Where(call => call.PcMethod == "UnpatchSelf").Select(call => call.PcMethod).Distinct(StringComparer.Ordinal)),
                }
            );
        }

        var lifecycleAttributes = allAttributes.Where(attribute => attribute.AttributeType.Namespace.StartsWith("IPA", StringComparison.Ordinal) && attribute.AttributeType.Name is "OnStartAttribute" or "OnExitAttribute" or "InitAttribute").ToArray();
        if (lifecycleAttributes.Length > 0)
        {
            adapters.Add(
                new JsonObject
                {
                    ["pcNamespace"] = "IPA",
                    ["questPackageId"] = "scotland2",
                    ["mechanism"] = "IPA lifecycle attributes are mapped to Scotland2 entrypoints",
                    ["status"] = "timing_mapped_body_not_lowered",
                    ["evidence"] = new JsonObject
                    {
                        ["lifecycleAttributeCount"] = lifecycleAttributes.Length,
                        ["attributes"] = Strings(lifecycleAttributes.Select(attribute => attribute.AttributeType.Name).Distinct(StringComparer.Ordinal)),
                        ["questEntrypoints"] = Strings(["late_load", "unload"]),
                    },
                }
            );
        }
        return adapters;
    }

    private static HashSet<string> GetAdapterHandledCallKeys(AssemblyDefinition sourceMod, IReadOnlyList<PcMethodSignature> sourceCalls)
    {
        var hasHarmonyPatchMetadata = CecilTypeWalker
            .Flatten(sourceMod.MainModule.Types)
            .SelectMany(type => type.CustomAttributes.Concat(type.Methods.SelectMany(method => method.CustomAttributes)))
            .Any(attribute => attribute.AttributeType.Namespace.StartsWith("HarmonyLib", StringComparison.Ordinal) && attribute.AttributeType.Name.Contains("HarmonyPatch", StringComparison.Ordinal));
        return hasHarmonyPatchMetadata ? sourceCalls.Where(call => call.PcType.StartsWith("HarmonyLib.", StringComparison.Ordinal) && call.PcMethod is ".ctor" or "PatchAll").Select(call => call.Key).ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
    }

    private static bool PackageContainsHeader(QpmPackage package, string header)
    {
        var relative = header.Replace('/', Path.DirectorySeparatorChar);
        return File.Exists(Path.Combine(package.SourceRoot, "include", relative)) || File.Exists(Path.Combine(package.SourceRoot, relative));
    }

    private static string CandidateMethodKey(JsonElement candidate)
    {
        var parameters = candidate.GetProperty("pcParameters").EnumerateArray().Select(item => item.GetString()!);
        return $"{candidate.GetProperty("pcAssembly").GetString()}|{candidate.GetProperty("pcType").GetString()}|{candidate.GetProperty("pcMethod").GetString()}|{candidate.GetProperty("pcGenericArity").GetInt32()}|{candidate.GetProperty("pcReturn").GetString()}|{candidate.GetProperty("pcStatic").GetBoolean()}|{string.Join(";", parameters)}";
    }

    private static string GetScopeAssemblyName(TypeReference type)
    {
        while (type.DeclaringType is not null)
            type = type.DeclaringType;
        return type.Scope?.Name ?? "";
    }
}
