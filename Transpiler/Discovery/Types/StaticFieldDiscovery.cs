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
    private void LoadLocalStaticFields(TypeDefinition type)
    {
        if (!IsTranspilerRelevantType(type))
            return;

        var defaults = ReadStaticDefaults(type);

        foreach (var field in type.Fields)
        {
            if (!field.IsStatic || field.IsLiteral)
                continue;

            if (field.CustomAttributes.Any(IsConfigAttribute))
                continue;

            var propertyName = TryGetAutoPropertyName(field.Name);
            if (propertyName != null && type.Properties.Any(property => property.Name == propertyName && property.CustomAttributes.Any(IsConfigAttribute)))
                continue;

            if (!CanEmitLocalStaticField(field.FieldType))
                continue;

            defaults.TryGetValue($"field:{field.Name}", out var defaultValue);
            _localStaticFields.Add(
                new LocalStaticFieldEntry
                {
                    Name = field.Name,
                    CppIdentifier = BuildUniqueLocalStaticIdentifier(type.FullName, field.Name),
                    DeclaringTypeFullName = type.FullName,
                    Type = field.FieldType,
                    DefaultValueCpp = defaultValue?.Code,
                    StringArrayElements = defaultValue?.StringArrayElements,
                }
            );
        }
    }
}
