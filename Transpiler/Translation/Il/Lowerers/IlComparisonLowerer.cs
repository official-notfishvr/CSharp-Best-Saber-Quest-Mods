using Mono.Cecil.Cil;

namespace Transpiler;

internal static class IlComparisonLowerer
{
    public static string Format(QuestValue left, QuestValue right, Code code)
    {
        var op = code switch
        {
            Code.Beq or Code.Beq_S or Code.Ceq => "==",
            Code.Bne_Un or Code.Bne_Un_S => "!=",
            Code.Bgt or Code.Bgt_S or Code.Bgt_Un or Code.Bgt_Un_S or Code.Cgt or Code.Cgt_Un => ">",
            Code.Bge or Code.Bge_S or Code.Bge_Un or Code.Bge_Un_S => ">=",
            Code.Blt or Code.Blt_S or Code.Blt_Un or Code.Blt_Un_S or Code.Clt or Code.Clt_Un => "<",
            Code.Ble or Code.Ble_S or Code.Ble_Un or Code.Ble_Un_S => "<=",
            _ => throw new InvalidOperationException($"Unsupported comparison opcode {code}."),
        };
        var unsignedOrUnordered = code is Code.Bne_Un or Code.Bne_Un_S or Code.Bgt_Un or Code.Bgt_Un_S or Code.Bge_Un or Code.Bge_Un_S or Code.Blt_Un or Code.Blt_Un_S or Code.Ble_Un or Code.Ble_Un_S or Code.Cgt_Un or Code.Clt_Un;
        if (!unsignedOrUnordered)
            return $"({left.Code} {op} {right.Code})";

        if (left.PcType is "System.Single" or "System.Double")
        {
            if (op == "!=")
                return $"({left.Code} != {right.Code})";
            return $"(std::isunordered({left.Code}, {right.Code}) || ({left.Code} {op} {right.Code}))";
        }

        if (TryUnsignedType(left.PcType, out var unsignedType))
            return $"(static_cast<{unsignedType}>({left.Code}) {op} static_cast<{unsignedType}>({right.Code}))";
        if (IsAlreadyUnsigned(left.PcType))
            return $"({left.Code} {op} {right.Code})";
        throw new InvalidOperationException($"Unsigned comparison {code} has no verified native representation for PC type '{left.PcType}'.");
    }

    private static bool IsAlreadyUnsigned(string type) => type is "System.Byte" or "System.UInt16" or "System.UInt32" or "System.UInt64" or "System.UIntPtr";

    private static bool TryUnsignedType(string type, out string nativeType)
    {
        nativeType = type switch
        {
            "System.SByte" => "uint8_t",
            "System.Int16" => "uint16_t",
            "System.Int32" => "uint32_t",
            "System.Int64" => "uint64_t",
            "System.IntPtr" => "uintptr_t",
            _ => "",
        };
        return nativeType.Length != 0;
    }
}
