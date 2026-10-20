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
    private void LoadModMetadata(TypeDefinition type)
    {
        var modAttribute = type.CustomAttributes.FirstOrDefault(IsModAttribute);
        if (modAttribute == null)
            return;

        if (modAttribute.ConstructorArguments.Count >= 2)
        {
            _modMetadata.Id = modAttribute.ConstructorArguments[0].Value?.ToString() ?? _modMetadata.Id;
            _modMetadata.Version = modAttribute.ConstructorArguments[1].Value?.ToString() ?? _modMetadata.Version;
        }
    }
}
