using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed partial class Transpiler
{
    private static string? ReadHookMethodName(CustomAttribute attribute)
    {
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Type.FullName == "System.Type")
                continue;

            if (argument.Value is string methodName)
                return methodName;
        }

        return ReadNamedAttributeString(attribute, "MethodName");
    }

    private static PatchBehavior ReadPatchKind(CustomAttribute attribute)
    {
        foreach (var argument in attribute.ConstructorArguments)
            if (argument.Type.FullName == "CoreMod.PatchKind" && int.TryParse(argument.Value?.ToString(), out var kind))
                return kind switch
                {
                    0 => PatchBehavior.Full,
                    1 => PatchBehavior.Prefix,
                    2 => PatchBehavior.Postfix,
                    _ => throw new InvalidOperationException($"Unknown patch kind '{kind}'."),
                };
        return PatchBehavior.Full;
    }

    private static PatchBehavior? ReadNamedAttributePatchBehavior(CustomAttribute attribute, string name)
    {
        foreach (var property in attribute.Properties)
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
                return ConvertPatchBehaviorValue(property.Argument.Value);
        }

        foreach (var field in attribute.Fields)
        {
            if (string.Equals(field.Name, name, StringComparison.Ordinal))
                return ConvertPatchBehaviorValue(field.Argument.Value);
        }

        return null;
    }

    private static PatchBehavior? ConvertPatchBehaviorValue(object? value)
    {
        return value switch
        {
            PatchBehavior phase => phase,
            int intValue => (PatchBehavior)intValue,
            byte byteValue => (PatchBehavior)byteValue,
            string text when Enum.TryParse(text, true, out PatchBehavior parsed) => parsed,
            _ => null,
        };
    }

    private TypeReference? ReadHookTargetType(CustomAttribute attribute)
    {
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Type.FullName == "System.Type")
                return NormalizeHookTargetType(argument.Value);
        }

        foreach (var property in attribute.Properties)
        {
            if (string.Equals(property.Name, "TargetType", StringComparison.Ordinal))
                return NormalizeHookTargetType(property.Argument.Value);
        }

        foreach (var field in attribute.Fields)
        {
            if (string.Equals(field.Name, "TargetType", StringComparison.Ordinal))
                return NormalizeHookTargetType(field.Argument.Value);
        }

        return null;
    }

    private TypeReference? NormalizeHookTargetType(object? value)
    {
        return value switch
        {
            TypeReference typeReference => _module?.ImportReference(typeReference) ?? typeReference,
            _ => null,
        };
    }
}
