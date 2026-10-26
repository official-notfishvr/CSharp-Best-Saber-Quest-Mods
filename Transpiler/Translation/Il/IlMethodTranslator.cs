using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal static class IlMethodTranslator
{
    public static IReadOnlyList<QuestLogStatement> TranslateModTypeMethod(MethodDefinition method, MappingCatalog catalog)
    {
        if (method.IsStatic || !method.HasBody || method.IsGetter || method.IsSetter)
            throw new InvalidOperationException($"Unsupported generated type method shape: {method.FullName}.");
        var arguments = new Dictionary<int, QuestValue> { [0] = new QuestValue("this", method.DeclaringType.FullName, false) };
        for (var i = 0; i < method.Parameters.Count; i++)
            arguments[i + 1] = new QuestValue(CppIdentifierForArgument(method.Parameters[i], i), method.Parameters[i].ParameterType.FullName, false);
        var nativeReturn = QuestApiTypeMapper.Map(method.ReturnType) ?? throw new InvalidOperationException($"No Quest representation for return type {method.ReturnType.FullName} in {method.FullName}.");
        var context = new IlTranslationContext(method, catalog, arguments, nativeReturn);
        return IlControlFlowLowerer.LowerRange(context, 0, method.Body.Instructions.Count, includeLocalDeclarations: true);
    }

    private static string CppIdentifierForArgument(ParameterDefinition parameter, int index) => System.Text.RegularExpressions.Regex.Replace(parameter.Name, "[^A-Za-z0-9_]", "_") is { Length: > 0 } name ? name : "arg" + index;

    public static IReadOnlyList<QuestLogStatement> TranslateBsmlAction(MethodDefinition method, MappingCatalog catalog)
    {
        if (method.IsStatic || method.ReturnType.MetadataType != MetadataType.Void || !method.HasBody || method.Parameters.Count != 0)
            throw new InvalidOperationException($"Unsupported BSML action shape: {method.FullName}; actions must be instance void methods without parameters.");
        var arguments = new Dictionary<int, QuestValue> { [0] = new QuestValue("this", method.DeclaringType.FullName, false) };
        var context = new IlTranslationContext(method, catalog, arguments, "void");
        return IlControlFlowLowerer.LowerRange(context, 0, method.Body.Instructions.Count, includeLocalDeclarations: true);
    }

    public static IReadOnlyList<QuestLogStatement> Translate(DiscoveredPatch patch, MappingCatalog catalog, TargetMapping mapping, MethodDefinition pcTarget)
    {
        var method = patch.Method;
        var validReturn = patch.Kind == HarmonyPatchKind.Postfix ? method.ReturnType.MetadataType == MetadataType.Void : method.ReturnType.MetadataType is MetadataType.Void or MetadataType.Boolean;
        if (!method.IsStatic || !validReturn || !method.HasBody)
            throw new InvalidOperationException($"Unsupported Harmony {patch.Kind.ToString().ToLowerInvariant()} shape: {method.FullName}; postfixes must be static void and prefixes must be static void or bool.");
        var nativeReturn =
            patch.Kind == HarmonyPatchKind.Postfix ? mapping.QuestReturn
            : method.ReturnType.MetadataType == MetadataType.Void ? "void"
            : "bool";
        var context = new IlTranslationContext(method, catalog, MapPatchArguments(method, pcTarget, mapping), nativeReturn);
        return IlControlFlowLowerer.LowerRange(context, 0, method.Body.Instructions.Count, includeLocalDeclarations: true);
    }

    private static IReadOnlyDictionary<int, QuestValue> MapPatchArguments(MethodDefinition patch, MethodDefinition target, TargetMapping mapping)
    {
        if (mapping.PcParameters.Length != target.Parameters.Count || mapping.QuestParameters.Length != target.Parameters.Count)
            throw new InvalidOperationException($"Target argument mapping is incomplete for {target.FullName}.");
        var result = new Dictionary<int, QuestValue>();
        for (var patchIndex = 0; patchIndex < patch.Parameters.Count; patchIndex++)
        {
            var parameter = patch.Parameters[patchIndex];
            if (parameter.Name == "__instance")
            {
                var instanceType = parameter.ParameterType is ByReferenceType instanceByReference ? instanceByReference.ElementType : parameter.ParameterType;
                if (mapping.QuestStatic || instanceType.FullName != target.DeclaringType.FullName || parameter.ParameterType is ByReferenceType)
                    throw new InvalidOperationException($"Harmony __instance parameter on {patch.FullName} does not match the PC target instance type {target.DeclaringType.FullName}.");
                result[patchIndex] = new QuestValue("self", parameter.ParameterType.FullName, false);
                continue;
            }
            if (parameter.Name == "__result")
            {
                if (target.ReturnType.MetadataType == MetadataType.Void || parameter.ParameterType is not ByReferenceType byReference || byReference.ElementType.FullName != target.ReturnType.FullName)
                    throw new InvalidOperationException($"Harmony __result parameter on {patch.FullName} must be a by-reference value matching the PC target return type {target.ReturnType.FullName}.");
                result[patchIndex] = new QuestValue("(&result)", target.ReturnType.FullName, false, IsByRef: true);
                continue;
            }
            var argIndex = parameter.Name.StartsWith("__", StringComparison.Ordinal) && int.TryParse(parameter.Name.AsSpan(2), out var specialIndex) ? specialIndex : target.Parameters.ToList().FindIndex(targetParameter => targetParameter.Name == parameter.Name);
            if (argIndex < 0 && patch.Parameters.Count == target.Parameters.Count && patch.Parameters.All(candidate => candidate.Name != "__instance"))
                argIndex = patchIndex;
            if (argIndex < 0 || argIndex >= target.Parameters.Count)
                throw new InvalidOperationException($"Harmony argument '{parameter.Name}' on {patch.FullName} does not match a parameter on PC target {target.FullName}.");
            var patchByReference = parameter.ParameterType as ByReferenceType;
            var targetParameterType = target.Parameters[argIndex].ParameterType;
            var targetByReference = targetParameterType as ByReferenceType;
            var patchValueType = patchByReference?.ElementType ?? parameter.ParameterType;
            var targetValueType = targetByReference?.ElementType ?? targetParameterType;
            if (patchValueType.FullName != targetValueType.FullName)
                throw new InvalidOperationException($"Harmony argument '{parameter.Name}' on {patch.FullName} does not match a parameter on PC target {target.FullName}.");
            var argumentCode =
                patchByReference is null ? (targetByReference is null ? $"arg{argIndex}" : $"(*arg{argIndex})")
                : targetByReference is null ? $"(&arg{argIndex})"
                : $"arg{argIndex}";
            result[patchIndex] = new QuestValue(argumentCode, patchValueType.FullName, false, IsByRef: patchByReference is not null);
        }
        return result;
    }
}
