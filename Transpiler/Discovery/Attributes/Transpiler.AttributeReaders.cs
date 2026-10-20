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
    private static void ValidateMenuButtonMethod(MethodDefinition method)
    {
        if (!method.IsStatic || method.ReturnType.FullName != "System.Void" || method.Parameters.Count != 0)
            throw new InvalidOperationException($"[MenuButton] methods must be static void with no parameters: {method.FullName}");
    }

    private static void ValidateGameplaySetupTabMethod(MethodDefinition method)
    {
        if (!method.IsStatic || method.ReturnType.FullName != "System.Void")
            throw new InvalidOperationException($"[GameplaySetupTab] methods must be static void: {method.FullName}");

        if (method.Parameters.Count != 2 || method.Parameters[0].ParameterType.FullName != "UnityEngine.GameObject" || method.Parameters[1].ParameterType.FullName != "System.Boolean")
        {
            throw new InvalidOperationException($"[GameplaySetupTab] methods must have signature static void Method(UnityEngine.GameObject, bool): {method.FullName}");
        }
    }

    private static bool ReadNamedAttributeBoolean(CustomAttribute attribute, string name)
    {
        foreach (var property in attribute.Properties)
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal) && property.Argument.Value is bool value)
                return value;
        }

        foreach (var field in attribute.Fields)
        {
            if (string.Equals(field.Name, name, StringComparison.Ordinal) && field.Argument.Value is bool value)
                return value;
        }

        return false;
    }

    private static int ReadNamedAttributeInt32(CustomAttribute attribute, string name, int defaultValue)
    {
        foreach (var property in attribute.Properties)
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
                return ConvertAttributeValueToInt32(property.Argument.Value, defaultValue);
        }

        foreach (var field in attribute.Fields)
        {
            if (string.Equals(field.Name, name, StringComparison.Ordinal))
                return ConvertAttributeValueToInt32(field.Argument.Value, defaultValue);
        }

        return defaultValue;
    }

    private static int ConvertAttributeValueToInt32(object? value, int defaultValue)
    {
        if (value is CustomAttributeArgument nestedArgument)
            value = nestedArgument.Value;

        return value switch
        {
            int intValue => intValue,
            byte byteValue => byteValue,
            sbyte sbyteValue => sbyteValue,
            short shortValue => shortValue,
            ushort ushortValue => ushortValue,
            _ => defaultValue,
        };
    }
}
