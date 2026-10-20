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
    private static bool IsPatchAttribute(CustomAttribute attribute) => attribute.AttributeType.Name is "PatchAttribute" or "Patch";

    private static bool IsModAttribute(CustomAttribute attribute) => attribute.AttributeType.Name is "ModAttribute" or "Mod";

    private static bool IsConfigAttribute(CustomAttribute attribute) => attribute.AttributeType.Name is "ConfigAttribute" or "Config";

    private static bool IsCustomTypeAttribute(CustomAttribute attribute) => attribute.AttributeType.Name is "CustomTypeAttribute" or "CustomType";

    private static bool IsMenuButtonAttribute(CustomAttribute attribute) => attribute.AttributeType.Name is "MenuButtonAttribute" or "MenuButton";

    private static bool IsGameplaySetupTabAttribute(CustomAttribute attribute) => attribute.AttributeType.Name is "GameplaySetupTabAttribute" or "GameplaySetupTab";

    private static string? ReadNamedAttributeString(CustomAttribute attribute, string name)
    {
        foreach (var property in attribute.Properties)
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
                return property.Argument.Value?.ToString();
        }

        foreach (var field in attribute.Fields)
        {
            if (string.Equals(field.Name, name, StringComparison.Ordinal))
                return field.Argument.Value?.ToString();
        }

        return null;
    }
}
