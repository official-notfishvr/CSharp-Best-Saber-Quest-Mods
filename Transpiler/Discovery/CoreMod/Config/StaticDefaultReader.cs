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
    private Dictionary<string, StaticDefaultValue> ReadStaticDefaults(TypeDefinition type)
    {
        var result = new Dictionary<string, StaticDefaultValue>(StringComparer.Ordinal);
        var staticCtor = type.Methods.FirstOrDefault(method => method.IsConstructor && method.IsStatic && method.HasBody);
        if (staticCtor?.Body == null)
            return result;

        var stack = new Stack<CppExpression>();
        foreach (var instruction in staticCtor.Body.Instructions)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Ldc_I4_M1:
                    stack.Push(new CppExpression { Code = "-1" });
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
                case Code.Dup:
                    if (stack.Count > 0)
                        stack.Push(stack.Peek());
                    break;
                case Code.Newarr:
                {
                    var elementType = (TypeReference)instruction.Operand;
                    if (stack.Count == 0 || elementType.FullName != "System.String")
                    {
                        stack.Clear();
                        break;
                    }

                    var lengthExpression = stack.Pop().Code;
                    if (!int.TryParse(lengthExpression, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) || length < 0)
                    {
                        stack.Clear();
                        break;
                    }

                    stack.Push(new CppExpression { Code = "", StringArrayElements = Enumerable.Repeat<string?>(null, length).ToList() });
                    break;
                }
                case Code.Stelem_Ref:
                {
                    if (stack.Count < 3)
                    {
                        stack.Clear();
                        break;
                    }

                    var value = stack.Pop();
                    var index = stack.Pop();
                    var array = stack.Pop();
                    if (array.StringArrayElements == null || !int.TryParse(index.Code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var elementIndex) || elementIndex < 0 || elementIndex >= array.StringArrayElements.Count)
                    {
                        stack.Clear();
                        break;
                    }

                    array.StringArrayElements[elementIndex] = value.Code;
                    break;
                }
                case Code.Stsfld:
                {
                    if (stack.Count == 0)
                        break;

                    var value = stack.Pop();
                    var field = (FieldReference)instruction.Operand;
                    var propertyName = TryGetAutoPropertyName(field.Name);
                    result[propertyName != null ? $"property:{propertyName}" : $"field:{field.Name}"] =
                        value.StringArrayElements != null ? new StaticDefaultValue { StringArrayElements = value.StringArrayElements.Select(item => item ?? CppLiteral.String("")).ToArray() } : new StaticDefaultValue { Code = NormalizeDefaultValue(field.FieldType, value.Code) };
                    break;
                }
                case Code.Ret:
                case Code.Nop:
                    break;
                default:
                    stack.Clear();
                    break;
            }
        }

        return result;
    }

    private string NormalizeDefaultValue(TypeReference type, string value)
    {
        return type.FullName == "System.Boolean" ? (value == "0" ? "false" : "true") : value;
    }
}
