using Mono.Cecil;

namespace Transpiler;

internal static partial class QuestTranspilationWorkflow
{
    static PatchDiscoveryResult DiscoverPatches(AssemblyDefinition assembly)
    {
        var found = new List<DiscoveredPatch>();
        var unsupported = new List<string>();
        foreach (var type in CecilTypeWalker.Flatten(assembly.MainModule.Types))
        {
            var classPatch = type.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
            if (classPatch is null && !type.Methods.Any(method => method.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch")))
                continue;
            foreach (var method in type.Methods.Where(IsHarmonyPatchMethod))
            {
                var methodPatch = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
                var kind = method.CustomAttributes.FirstOrDefault(attribute => attribute.AttributeType.Namespace == "HarmonyLib" && attribute.AttributeType.Name is "HarmonyPrefix" or "HarmonyPostfix" or "HarmonyTranspiler" or "HarmonyFinalizer")?.AttributeType.Name ?? method.Name;
                if (kind is not ("HarmonyPostfix" or "Postfix" or "HarmonyPrefix" or "Prefix"))
                {
                    unsupported.Add($"{type.FullName}.{method.Name}: Harmony {kind} methods are not lowered by the current Quest hook backend.");
                    continue;
                }
                var patchKind = kind is "HarmonyPrefix" or "Prefix" ? HarmonyPatchKind.Prefix : HarmonyPatchKind.Postfix;
                var attribute = methodPatch is { ConstructorArguments.Count: > 0 } ? methodPatch : classPatch ?? methodPatch;
                if (attribute is null)
                {
                    unsupported.Add($"{type.FullName}.{method.Name}: postfix has no HarmonyPatch target declaration.");
                    continue;
                }
                TypeReference? target = null;
                string? targetName = null;
                string[]? targetParameters = null;
                foreach (var argument in attribute.ConstructorArguments)
                {
                    if (argument.Value is TypeReference typeReference)
                        target = typeReference;
                    else if (argument.Value is string methodName)
                        targetName = methodName;
                    else if (argument.Value is CustomAttributeArgument[] typeArguments)
                        targetParameters = typeArguments.Select(item => (item.Value as TypeReference)?.FullName ?? throw new InvalidOperationException($"{type.FullName}.{method.Name}: Harmony target argument type is not a resolvable TypeReference.")).ToArray();
                }
                if (target is null || targetName is null)
                {
                    var selector = type.Methods.FirstOrDefault(candidate => candidate.Name == "TargetMethod" && candidate.IsStatic);
                    if (selector is null)
                    {
                        unsupported.Add($"{type.FullName}.{method.Name}: Harmony patch does not declare a static target or TargetMethod selector.");
                        continue;
                    }
                    found.Add(new DiscoveredPatch(type, method, "", "", patchKind, selector));
                    continue;
                }
                found.Add(new DiscoveredPatch(type, method, target.FullName, targetName, patchKind, TargetParameterTypes: targetParameters));
            }
        }
        return new PatchDiscoveryResult(found, unsupported);

        static bool IsHarmonyPatchMethod(MethodDefinition method) =>
            method.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "HarmonyLib.HarmonyPostfix")
            || method.Name is "Prefix" or "Postfix" or "Transpiler" or "Finalizer"
            || method.CustomAttributes.Any(attribute => attribute.AttributeType.FullName is "HarmonyLib.HarmonyPrefix" or "HarmonyLib.HarmonyTranspiler" or "HarmonyLib.HarmonyFinalizer");
    }

    static MethodDefinition ResolvePcTarget(DiscoveredPatch patch, List<AssemblyDefinition> assemblies, MappingCatalog catalog)
    {
        if (patch.DynamicTargetSelector is not null)
            return ResolveDynamicTarget(patch.DynamicTargetSelector, assemblies);
        var candidates = assemblies
            .SelectMany(assembly => CecilTypeWalker.Flatten(assembly.MainModule.Types))
            .Where(type => type.FullName == patch.TargetType)
            .SelectMany(type => type.Methods.Where(method => method.Name == patch.TargetMethod))
            .Where(method => patch.TargetParameterTypes is null || method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(patch.TargetParameterTypes, StringComparer.Ordinal))
            .Where(method => HarmonyTargetParametersMatch(patch, method))
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException($"Could not resolve PC target {patch.TargetType}.{patch.TargetMethod} in the selected PC managed assemblies.");
        var mapped = candidates.Where(c => catalog.HasExactPcMapping(c)).ToList();
        if (mapped.Count == 0)
        {
            var apiMatches = candidates.Where(candidate => catalog.ResolveApiCall(candidate) is not null).ToArray();
            throw new InvalidOperationException($"No full-signature mapping exists for PC target candidates: {string.Join("; ", candidates.Select(Signature))}{(apiMatches.Length == 0 ? "" : $"; {apiMatches.Length} exact API candidate(s) were found but are not target mappings")}. ");
        }
        if (mapped.Count != 1)
            throw new InvalidOperationException($"PC target {patch.TargetType}.{patch.TargetMethod} has multiple mapped overloads; the Harmony attribute must identify one overload. Candidates: {string.Join("; ", mapped.Select(Signature))}.");
        return mapped[0];
    }

    static bool HarmonyTargetParametersMatch(DiscoveredPatch patch, MethodDefinition target)
    {
        foreach (var (parameter, patchIndex) in patch.Method.Parameters.Select((parameter, index) => (parameter, index)))
        {
            if (parameter.Name is "__instance" or "__result" or "__state" || parameter.Name.StartsWith("____", StringComparison.Ordinal))
                continue;
            var targetIndex = parameter.Name.StartsWith("__", StringComparison.Ordinal) && int.TryParse(parameter.Name.AsSpan(2), out var explicitIndex) ? explicitIndex : target.Parameters.ToList().FindIndex(candidate => candidate.Name == parameter.Name);
            if (targetIndex < 0 && patch.Method.Parameters.Count == target.Parameters.Count && !patch.Method.Parameters.Any(candidate => candidate.Name == "__instance"))
                targetIndex = patchIndex;
            if (targetIndex < 0)
                continue;
            if (targetIndex >= target.Parameters.Count)
                return false;
            var patchType = parameter.ParameterType is ByReferenceType patchByReference ? patchByReference.ElementType : parameter.ParameterType;
            var targetType = target.Parameters[targetIndex].ParameterType is ByReferenceType targetByReference ? targetByReference.ElementType : target.Parameters[targetIndex].ParameterType;
            if (patchType.FullName != targetType.FullName)
                return false;
        }
        return true;
    }

    static MethodDefinition ResolveDynamicTarget(MethodDefinition selector, List<AssemblyDefinition> assemblies)
    {
        var instructions = selector.Body.Instructions;
        var typeToken =
            instructions.FirstOrDefault(instruction => instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldtoken && instruction.Operand is TypeReference)?.Operand as TypeReference ?? throw new InvalidOperationException($"Dynamic Harmony selector {selector.FullName} does not load a type token.");
        var methodPredicateReference =
            instructions.Where(instruction => instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldftn).Select(instruction => instruction.Operand as MethodReference).FirstOrDefault(reference => reference?.Name.Contains("TargetMethod", StringComparison.Ordinal) == true)
            ?? throw new InvalidOperationException($"Dynamic Harmony selector {selector.FullName} does not use a supported compiler-generated method predicate.");
        var methodPredicate = methodPredicateReference.Resolve() ?? throw new InvalidOperationException($"Could not resolve method predicate {methodPredicateReference.FullName}.");
        var targetName =
            methodPredicate.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldstr)?.Operand as string ?? throw new InvalidOperationException($"Dynamic Harmony selector {selector.FullName} does not compare method names to a string constant.");
        var aggregation =
            methodPredicate.Body.Instructions.Where(instruction => instruction.Operand is MethodReference).Select(instruction => (MethodReference)instruction.Operand).FirstOrDefault(reference => reference.DeclaringType.FullName == "System.Linq.Enumerable" && reference.Name is "All" or "Any")
            ?? throw new InvalidOperationException($"Dynamic Harmony selector {selector.FullName} must use Enumerable.All or Enumerable.Any for its parameter predicate.");
        var parameterPredicateReference =
            methodPredicate.Body.Instructions.Where(instruction => instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldftn).Select(instruction => instruction.Operand as MethodReference).FirstOrDefault(reference => reference is not null && reference.FullName != methodPredicateReference.FullName)
            ?? throw new InvalidOperationException($"Dynamic Harmony selector {selector.FullName} does not provide a parameter predicate.");
        var parameterPredicate = parameterPredicateReference.Resolve() ?? throw new InvalidOperationException($"Could not resolve parameter predicate {parameterPredicateReference.FullName}.");
        var parameterTypeToken =
            parameterPredicate.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldtoken && instruction.Operand is TypeReference)?.Operand as TypeReference
            ?? throw new InvalidOperationException($"Dynamic Harmony selector {selector.FullName} parameter predicate does not load a type token.");
        var typeComparison = parameterPredicate
            .Body.Instructions.Where(instruction => instruction.Operand is MethodReference)
            .Select(instruction => (MethodReference)instruction.Operand)
            .FirstOrDefault(reference => reference.DeclaringType.FullName == "System.Type" && reference.Name is "op_Equality" or "op_Inequality");
        if (typeComparison is null)
            throw new InvalidOperationException($"Dynamic Harmony selector {selector.FullName} parameter predicate does not compare parameter types for equality.");

        var candidates = assemblies
            .SelectMany(assembly => CecilTypeWalker.Flatten(assembly.MainModule.Types))
            .Where(type => type.FullName == typeToken.FullName)
            .SelectMany(type => type.Methods.Where(method => method.Name == targetName))
            .Where(method =>
                aggregation.Name == "All" ? method.Parameters.All(parameter => CompareParameterType(parameter.ParameterType, parameterTypeToken, typeComparison.Name)) : method.Parameters.Any(parameter => CompareParameterType(parameter.ParameterType, parameterTypeToken, typeComparison.Name))
            )
            .ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException($"Dynamic Harmony selector {selector.FullName} did not match a PC method on {typeToken.FullName}.");
        if (candidates.Length > 1)
            throw new InvalidOperationException($"Dynamic Harmony selector {selector.FullName} matched multiple PC methods on {typeToken.FullName}: {string.Join("; ", candidates.Select(Signature))}.");
        return candidates[0];
    }

    static bool CompareParameterType(TypeReference candidate, TypeReference expected, string operation) => operation == "op_Equality" ? candidate.FullName == expected.FullName : candidate.FullName != expected.FullName;

    static string TypeName(TypeReference type) => type.FullName.Replace('/', '+');

    static string Signature(MethodDefinition method) => $"{method.Module.Assembly.Name.Name}:{method.DeclaringType.FullName}.{method.Name}({string.Join(",", method.Parameters.Select(p => TypeName(p.ParameterType)))}):{TypeName(method.ReturnType)}:{(method.IsStatic ? "static" : "instance")}";
}
