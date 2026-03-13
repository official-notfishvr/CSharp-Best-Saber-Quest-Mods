using System.Collections.Generic;
using Mono.Cecil;

namespace Transpiler;

internal sealed class GeneratedFile
{
    public required string Path { get; init; }
    public required string Content { get; init; }
}

internal sealed class ModInfo
{
    public string Id { get; set; } = "mod";
    public string Version { get; set; } = "1.0.0";
}

internal sealed class ConfigValueInfo
{
    public required string Name { get; init; }
    public required string CppIdentifier { get; set; }
    public required string DeclaringTypeFullName { get; init; }
    public required TypeReference Type { get; init; }
    public string Description { get; init; } = "";
    public string? DefaultValueCpp { get; init; }
}

internal sealed class HookInfo
{
    public required string HookName { get; init; }
    public required string TargetMethod { get; init; }
    public required TypeReference TargetType { get; init; }
    public required MethodDefinition Method { get; init; }
    public bool IsConstructor { get; init; }
}

internal sealed class CppValue
{
    public required string Code { get; init; }
    public TypeReference? Type { get; init; }
    public bool HasSideEffects { get; init; }
    public bool PreferAutoDeclaration { get; init; }
}
