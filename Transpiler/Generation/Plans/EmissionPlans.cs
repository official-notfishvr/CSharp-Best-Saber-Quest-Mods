using Mono.Cecil;

namespace Transpiler;

internal sealed record PatchEmission(PatchDefinition Target, PatchDefinition? Before, PatchDefinition? After, IlMethodTranslator? ReplacementBody, IlMethodTranslator? BeforeBody, IlMethodTranslator? AfterBody);

internal sealed record HelperMethodEmission(MethodDefinition Method, string FunctionName, IlMethodTranslator Body);

internal sealed record CustomTypeMethodEmission(CustomTypeEntry CustomType, CustomTypeMethodEntry Method, IlMethodTranslator Body);
