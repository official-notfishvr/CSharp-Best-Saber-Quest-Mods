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
    private void LoadConfigs(TypeDefinition type)
    {
        var defaults = ReadStaticDefaults(type);

        foreach (var property in type.Properties)
        {
            var configAttribute = property.CustomAttributes.FirstOrDefault(IsConfigAttribute);
            if (configAttribute == null)
                continue;

            defaults.TryGetValue($"property:{property.Name}", out var defaultValue);
            var defaultValueCpp = defaultValue?.Code;

            _configValues.Add(
                new ConfigEntry
                {
                    Name = property.Name,
                    CppIdentifier = CppIdentifier.Sanitize(property.Name),
                    DeclaringTypeFullName = type.FullName,
                    Type = property.PropertyType,
                    Description = ReadNamedAttributeString(configAttribute, "Description") ?? "",
                    DefaultValueCpp = defaultValueCpp,
                }
            );
        }

        foreach (var field in type.Fields)
        {
            var configAttribute = field.CustomAttributes.FirstOrDefault(IsConfigAttribute);
            if (configAttribute == null)
                continue;

            defaults.TryGetValue($"field:{field.Name}", out var defaultValue);
            var defaultValueCpp = defaultValue?.Code;

            _configValues.Add(
                new ConfigEntry
                {
                    Name = field.Name,
                    CppIdentifier = CppIdentifier.Sanitize(field.Name),
                    DeclaringTypeFullName = type.FullName,
                    Type = field.FieldType,
                    Description = ReadNamedAttributeString(configAttribute, "Description") ?? "",
                    DefaultValueCpp = defaultValueCpp,
                }
            );
        }
    }
}
