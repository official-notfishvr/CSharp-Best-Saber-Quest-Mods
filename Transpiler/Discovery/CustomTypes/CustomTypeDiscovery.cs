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
    private void LoadCustomType(TypeDefinition type)
    {
        if (!type.CustomAttributes.Any(IsCustomTypeAttribute))
            return;

        if (type.BaseType == null)
            throw new InvalidOperationException($"[CustomType] requires a base type: {type.FullName}");

        var defaults = ReadInstanceFieldDefaults(type);
        var fields = new List<CustomTypeFieldEntry>();
        foreach (var field in type.Fields)
        {
            if (field.IsStatic || field.IsLiteral || field.Name.StartsWith("<", StringComparison.Ordinal))
                continue;

            fields.Add(
                new CustomTypeFieldEntry
                {
                    Name = field.Name,
                    CppType = _typeSystem.MapType(field.FieldType),
                    DefaultValueCpp = defaults.TryGetValue(field.Name, out var defaultValue) ? defaultValue : null,
                }
            );
        }

        var methods = new List<CustomTypeMethodEntry>();
        foreach (var method in type.Methods)
        {
            if (method.IsStatic || !method.HasBody || method.IsConstructor || method.IsGetter || method.IsSetter)
                continue;

            methods.Add(new CustomTypeMethodEntry { Method = method, CppName = CppIdentifier.Sanitize(method.Name) });
        }

        _customTypes.Add(
            new CustomTypeEntry
            {
                Type = type,
                CppNamespace = _typeSystem.MapNamespace(type.Namespace),
                CppName = _typeSystem.ComposeTypeName(type),
                BaseCppType = _typeSystem.MapType(type.BaseType).TrimEnd('*'),
                DllName = _module?.Assembly.Name.Name ?? type.Module.Assembly.Name.Name,
                Fields = fields,
                Methods = methods,
            }
        );
    }

    private Dictionary<string, string> ReadInstanceFieldDefaults(TypeDefinition type)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var ctor = type.Methods.FirstOrDefault(method => method.IsConstructor && !method.IsStatic && method.HasBody && method.Parameters.Count == 0);
        if (ctor?.Body == null)
            return result;

        var stack = new Stack<CppExpression>();
        foreach (var instruction in ctor.Body.Instructions)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Ldarg_0:
                    stack.Push(new CppExpression { Code = "this" });
                    break;
                case Code.Ldc_I4_0:
                case Code.Ldc_I4_1:
                case Code.Ldc_I4_2:
                case Code.Ldc_I4_3:
                case Code.Ldc_I4_4:
                case Code.Ldc_I4_5:
                case Code.Ldc_I4_6:
                case Code.Ldc_I4_7:
                case Code.Ldc_I4_8:
                    stack.Push(new CppExpression { Code = ((int)instruction.OpCode.Code - (int)Code.Ldc_I4_0).ToString(CultureInfo.InvariantCulture) });
                    break;
                case Code.Ldc_I4:
                case Code.Ldc_I4_S:
                    stack.Push(new CppExpression { Code = Convert.ToInt32(instruction.Operand, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) });
                    break;
                case Code.Ldstr:
                    stack.Push(new CppExpression { Code = CppLiteral.String((string)instruction.Operand) });
                    break;
                case Code.Stfld:
                {
                    if (stack.Count < 2)
                    {
                        stack.Clear();
                        break;
                    }

                    var value = stack.Pop();
                    var target = stack.Pop();
                    var field = (FieldReference)instruction.Operand;
                    if (target.Code == "this" && field.DeclaringType.FullName == type.FullName)
                        result[field.Name] = NormalizeDefaultValue(field.FieldType, value.Code);
                    break;
                }
                case Code.Call:
                case Code.Callvirt:
                case Code.Ret:
                case Code.Nop:
                    stack.Clear();
                    break;
                default:
                    stack.Clear();
                    break;
            }
        }

        return result;
    }
}
