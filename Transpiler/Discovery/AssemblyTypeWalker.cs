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
    private void ProcessType(TypeDefinition type)
    {
        LoadModMetadata(type);
        LoadConfigs(type);
        LoadLocalStaticFields(type);
        LoadCustomType(type);
        LoadPatches(type);
        LoadBsmlRegistrations(type);
        LoadHelperMethods(type);

        foreach (var nestedType in type.NestedTypes)
            ProcessType(nestedType);
    }
}
