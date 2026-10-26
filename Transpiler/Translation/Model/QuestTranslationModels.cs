using Mono.Cecil;

namespace Transpiler;

internal sealed record QuestLogStatement(string Message, string QuestLevel, string SourceCall, string? QuestExpression, ApiCallMapping? ApiMapping, string? RawCpp = null, QuestConfigField? ConfigField = null, IReadOnlyList<QuestStaticField>? StaticFields = null);

internal sealed record QuestValue(string Code, string PcType, bool IsStringLiteral, ApiCallMapping? ApiMapping = null, bool IsByRef = false, string? DeferredBoxedPrimitiveType = null, MethodDefinition? FunctionPointerTarget = null);

internal sealed record QuestConfigField(string Name, string CppType, string Label, string DefaultValue);

internal sealed record QuestStaticField(string Name, string Declaration);
