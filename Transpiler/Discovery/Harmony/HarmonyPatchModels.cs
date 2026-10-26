namespace Transpiler;

internal enum HarmonyPatchKind
{
    Prefix,
    Postfix,
}

internal sealed record DiscoveredPatch(Mono.Cecil.TypeDefinition DeclaringType, Mono.Cecil.MethodDefinition Method, string TargetType, string TargetMethod, HarmonyPatchKind Kind, Mono.Cecil.MethodDefinition? DynamicTargetSelector = null, string[]? TargetParameterTypes = null);

internal sealed record PatchDiscoveryResult(List<DiscoveredPatch> Patches, List<string> Unsupported);
