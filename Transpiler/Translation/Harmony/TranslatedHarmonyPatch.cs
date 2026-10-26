namespace Transpiler;

internal sealed record TranslatedHarmonyPatch(TargetMapping Mapping, DiscoveredPatch Patch, IReadOnlyList<QuestLogStatement> Statements);
