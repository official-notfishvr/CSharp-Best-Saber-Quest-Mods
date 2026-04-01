using System;
using System.Collections.Generic;
using System.Text;
using Mono.Cecil;

namespace Transpiler;

internal sealed class GeneratedArtifact
{
    public required string Path { get; init; }
    public required string Content { get; init; }
}

internal sealed class ModMetadata
{
    public string Id { get; set; } = "mod";
    public string Version { get; set; } = "1.0.0";
}

internal sealed class ConfigEntry
{
    public required string Name { get; init; }
    public required string CppIdentifier { get; set; }
    public required string DeclaringTypeFullName { get; init; }
    public required TypeReference Type { get; init; }
    public string Description { get; init; } = "";
    public string? DefaultValueCpp { get; init; }
}

internal enum HookPhase
{
    Full = 0,
    Prefix = 1,
    Postfix = 2,
}

internal sealed class HookDefinition
{
    public required string HookName { get; init; }
    public required string TargetMethod { get; init; }
    public required TypeReference TargetType { get; init; }
    public required MethodDefinition Method { get; init; }
    public bool IsConstructor { get; init; }
    public HookPhase Phase { get; init; } = HookPhase.Full;
}

internal sealed class CppExpression
{
    public required string Code { get; init; }
    public TypeReference? Type { get; init; }
    public bool HasSideEffects { get; init; }
    public bool PreferAutoDeclaration { get; init; }
}

internal sealed class CppCodeWriter
{
    private readonly StringBuilder _builder = new();

    public void WriteLine(string line = "")
    {
        _builder.AppendLine(line);
    }

    public override string ToString() => _builder.ToString();
}

internal static class CppLiteral
{
    public static string String(string value)
    {
        return $"il2cpp_utils::newcsstr(\"{Escape(value)}\")";
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);
    }
}

internal static class CppIdentifier
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "alignas",
        "alignof",
        "and",
        "asm",
        "auto",
        "bool",
        "break",
        "case",
        "catch",
        "char",
        "class",
        "const",
        "constexpr",
        "continue",
        "default",
        "delete",
        "do",
        "double",
        "else",
        "enum",
        "explicit",
        "export",
        "extern",
        "false",
        "float",
        "for",
        "friend",
        "goto",
        "if",
        "inline",
        "int",
        "long",
        "namespace",
        "new",
        "noexcept",
        "nullptr",
        "operator",
        "private",
        "protected",
        "public",
        "register",
        "reinterpret_cast",
        "return",
        "short",
        "signed",
        "sizeof",
        "static",
        "struct",
        "switch",
        "template",
        "this",
        "throw",
        "true",
        "try",
        "typedef",
        "typename",
        "union",
        "unsigned",
        "using",
        "virtual",
        "void",
        "volatile",
        "while",
    };

    public static string Sanitize(string? name, string fallback = "value")
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;

        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');

        var sanitized = builder.ToString();
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = fallback;

        if (char.IsDigit(sanitized[0]))
            sanitized = "_" + sanitized;

        return Keywords.Contains(sanitized) ? $"{sanitized}_" : sanitized;
    }
}
