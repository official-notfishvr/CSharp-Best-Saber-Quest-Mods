using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal static class IlArrayLowerer
{
    public static bool TryLower(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack, out QuestLogStatement? statement)
    {
        statement = null;
        switch (instruction.OpCode.Code)
        {
            case Code.Newarr:
            {
                if (instruction.Operand is not TypeReference elementType || !TryCppArrayType(elementType, out var elementCppType))
                    throw Unsupported(context.Method, instruction, "array element type has no Quest ArrayW representation");
                var length = Pop(context, instruction, stack);
                if (length.PcType is not ("System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64"))
                    throw Unsupported(context.Method, instruction, "array length must be an integer");
                stack.Push(new QuestValue($"::ArrayW<{elementCppType}, ::Array<{elementCppType}>*>(static_cast<size_t>({length.Code}))", elementType.FullName + "[]", false));
                return true;
            }
            case Code.Ldlen:
            {
                var array = Pop(context, instruction, stack);
                RequireArray(context, instruction, array);
                stack.Push(new QuestValue($"({array.Code}).size()", "System.UInt64", false));
                return true;
            }
            case Code.Ldelema:
            {
                if (instruction.Operand is not TypeReference addressType || !TryCppType(addressType.FullName, out _))
                    throw Unsupported(context.Method, instruction, "only addresses of primitive value array elements are supported");
                var index = Pop(context, instruction, stack);
                var array = Pop(context, instruction, stack);
                var element = RequireArray(context, instruction, array);
                RequireElement(context, instruction, element, addressType.FullName);
                RequireIndex(context, instruction, index);
                stack.Push(new QuestValue($"&({array.Code}[static_cast<size_t>({index.Code})])", element, false, array.ApiMapping ?? index.ApiMapping, IsByRef: true));
                return true;
            }
            case Code.Ldelem_I1:
            case Code.Ldelem_U1:
            case Code.Ldelem_I2:
            case Code.Ldelem_U2:
            case Code.Ldelem_I4:
            case Code.Ldelem_U4:
            case Code.Ldelem_I8:
            case Code.Ldelem_I:
            case Code.Ldelem_R4:
            case Code.Ldelem_R8:
            case Code.Ldelem_Any:
                LowerLoad(context, instruction, stack);
                return true;
            case Code.Ldelem_Ref:
                LowerLoad(context, instruction, stack);
                return true;
            case Code.Stelem_I:
            case Code.Stelem_I1:
            case Code.Stelem_I2:
            case Code.Stelem_I4:
            case Code.Stelem_I8:
            case Code.Stelem_R4:
            case Code.Stelem_R8:
            case Code.Stelem_Any:
                LowerStore(context, instruction, stack, out statement);
                return true;
            case Code.Stelem_Ref:
                LowerStore(context, instruction, stack, out statement);
                return true;
            default:
                return false;
        }
    }

    public static bool TryCppType(string type, out string cppType)
    {
        cppType = type switch
        {
            "System.Boolean" => "bool",
            "System.Byte" => "uint8_t",
            "System.SByte" => "int8_t",
            "System.Int16" => "int16_t",
            "System.UInt16" => "uint16_t",
            "System.Int32" => "int32_t",
            "System.UInt32" => "uint32_t",
            "System.Int64" => "int64_t",
            "System.UInt64" => "uint64_t",
            "System.Single" => "float",
            "System.Double" => "double",
            _ => "",
        };
        return cppType.Length != 0;
    }

    public static bool TryCppArrayType(string type, out string cppType)
    {
        if (type == "System.String")
        {
            cppType = "::StringW";
            return true;
        }
        if (TryCppType(type, out cppType))
            return true;
        if (type == "System.String")
        {
            cppType = "::StringW";
            return true;
        }
        if (type.StartsWith("System.", StringComparison.Ordinal))
        {
            cppType = "";
            return false;
        }
        cppType = "::" + string.Join("::", type.Split('.')) + "*";
        return true;
    }

    private static bool TryCppArrayType(TypeReference type, out string cppType)
    {
        if (TryCppType(type.FullName, out cppType))
            return true;
        cppType = QuestApiTypeMapper.Map(type) ?? "";
        return cppType.Length > 0;
    }

    private static void LowerLoad(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack)
    {
        var index = Pop(context, instruction, stack);
        var array = Pop(context, instruction, stack);
        var element = RequireArray(context, instruction, array);
        var expected =
            instruction.OpCode.Code == Code.Ldelem_Any ? (instruction.Operand as TypeReference)?.FullName
            : instruction.OpCode.Code == Code.Ldelem_Ref ? element
            : instruction.OpCode.Code switch
            {
                Code.Ldelem_I1 => "System.SByte",
                Code.Ldelem_U1 => "System.Byte",
                Code.Ldelem_I2 => "System.Int16",
                Code.Ldelem_U2 => "System.UInt16",
                Code.Ldelem_I4 => "System.Int32",
                Code.Ldelem_U4 => "System.UInt32",
                Code.Ldelem_I8 => "System.Int64",
                Code.Ldelem_I => "System.Int32",
                Code.Ldelem_R4 => "System.Single",
                Code.Ldelem_R8 => "System.Double",
                _ => null,
            };
        RequireElement(context, instruction, element, expected, instruction.OpCode.Code is Code.Ldelem_I1 or Code.Ldelem_U1);
        RequireIndex(context, instruction, index);
        stack.Push(new QuestValue($"({array.Code})[static_cast<size_t>({index.Code})]", element, false, array.ApiMapping ?? index.ApiMapping));
    }

    private static void LowerStore(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack, out QuestLogStatement? statement)
    {
        statement = null;
        var value = Pop(context, instruction, stack);
        var index = Pop(context, instruction, stack);
        var array = Pop(context, instruction, stack);
        var element = RequireArray(context, instruction, array);
        var expected =
            instruction.OpCode.Code == Code.Stelem_Any ? (instruction.Operand as TypeReference)?.FullName
            : instruction.OpCode.Code == Code.Stelem_Ref ? "System.String"
            : instruction.OpCode.Code switch
            {
                Code.Stelem_I1 => "System.SByte",
                Code.Stelem_I2 => "System.Int16",
                Code.Stelem_I4 => "System.Int32",
                Code.Stelem_I8 => "System.Int64",
                Code.Stelem_I => "System.Int32",
                Code.Stelem_R4 => "System.Single",
                Code.Stelem_R8 => "System.Double",
                _ => null,
            };
        RequireElement(context, instruction, element, expected, instruction.OpCode.Code == Code.Stelem_I1);
        RequireIndex(context, instruction, index);
        var isStringNull = element == "System.String" && value.PcType == "System.Object" && value.Code == "nullptr" && !value.IsByRef;
        if ((value.PcType != element && !isStringNull) || value.IsByRef)
            throw Unsupported(context.Method, instruction, "stored value does not exactly match the array element type");
        var valueCode =
            isStringNull ? "::StringW{}"
            : value.PcType == "System.String" && value.IsStringLiteral ? StringLiteral(value.Code)
            : value.Code;
        statement = Raw($"({array.Code})[static_cast<size_t>({index.Code})] = {valueCode};", "IL array assignment", value.ApiMapping ?? array.ApiMapping);
    }

    private static string RequireArray(IlTranslationContext context, Instruction instruction, QuestValue array)
    {
        if (array.IsByRef || !array.PcType.EndsWith("[]", StringComparison.Ordinal))
            throw Unsupported(context.Method, instruction, "array opcode requires a one-dimensional managed array");
        var elementName = array.PcType[..^2];
        if (!TryCppArrayType(elementName, out _))
            throw Unsupported(context.Method, instruction, "array element type has no Quest representation");
        return elementName;
    }

    private static void RequireElement(IlTranslationContext context, Instruction instruction, string element, string? expected, bool allowBooleanStorage = false)
    {
        var compatible = expected == element || (allowBooleanStorage && element == "System.Boolean" && expected is "System.Byte" or "System.SByte");
        if (expected is null || !TryCppArrayType(expected, out _) || !compatible)
            throw Unsupported(context.Method, instruction, "array opcode type does not exactly match its declared element type");
    }

    private static void RequireIndex(IlTranslationContext context, Instruction instruction, QuestValue index)
    {
        if (index.IsByRef || index.PcType is not ("System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64"))
            throw Unsupported(context.Method, instruction, "array index must be an integer");
    }

    private static string CppType(string type) => TryCppArrayType(type, out var cpp) ? cpp : throw new InvalidOperationException($"Unsupported array element type {type}.");

    private static string StringLiteral(string value) => "::StringW(\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\")";

    private static QuestValue Pop(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack) => stack.Count == 0 ? throw Unsupported(context.Method, instruction, "evaluation stack underflow") : stack.Pop();

    private static QuestLogStatement Raw(string code, string source, ApiCallMapping? mapping = null) => new("", "", source, null, mapping, code);

    private static InvalidOperationException Unsupported(MethodDefinition method, Instruction instruction, string reason) => new($"Unsupported IL in {method.FullName} at IL_{instruction.Offset:x4} ({instruction.OpCode.Code}): {reason}.");
}
