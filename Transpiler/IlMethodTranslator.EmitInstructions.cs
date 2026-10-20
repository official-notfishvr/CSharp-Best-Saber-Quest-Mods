using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed partial class IlMethodTranslator
{
    private void EmitInstruction(Instruction instruction, int indentLevel)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Nop:
                return;
            case Code.Ldarg_0:
                PushArgument(0);
                return;
            case Code.Ldarg_1:
                PushArgument(1);
                return;
            case Code.Ldarg_2:
                PushArgument(2);
                return;
            case Code.Ldarg_3:
                PushArgument(3);
                return;
            case Code.Ldarg:
            case Code.Ldarg_S:
                PushArgument(GetIlArgumentIndex((ParameterDefinition)instruction.Operand));
                return;
            case Code.Ldarga:
            case Code.Ldarga_S:
                PushArgumentAddress(GetIlArgumentIndex((ParameterDefinition)instruction.Operand));
                return;
            case Code.Ldloc_0:
            case Code.Ldloc_1:
            case Code.Ldloc_2:
            case Code.Ldloc_3:
                PushLocal((int)instruction.OpCode.Code - (int)Code.Ldloc_0);
                return;
            case Code.Ldloc:
            case Code.Ldloc_S:
                PushLocal(((VariableDefinition)instruction.Operand).Index);
                return;
            case Code.Ldloca:
            case Code.Ldloca_S:
                PushLocalAddress(((VariableDefinition)instruction.Operand).Index);
                return;
            case Code.Stloc_0:
            case Code.Stloc_1:
            case Code.Stloc_2:
            case Code.Stloc_3:
                StoreLocal((int)instruction.OpCode.Code - (int)Code.Stloc_0, indentLevel);
                return;
            case Code.Stloc:
            case Code.Stloc_S:
                StoreLocal(((VariableDefinition)instruction.Operand).Index, indentLevel);
                return;
            case Code.Starg:
            case Code.Starg_S:
                StoreArgument(GetIlArgumentIndex((ParameterDefinition)instruction.Operand), indentLevel);
                return;
            case Code.Ldc_I4_M1:
                _stack.Push(new CppExpression { Code = "-1", Type = _method.Module.TypeSystem.Int32 });
                return;
            case Code.Ldc_I4_0:
            case Code.Ldc_I4_1:
            case Code.Ldc_I4_2:
            case Code.Ldc_I4_3:
            case Code.Ldc_I4_4:
            case Code.Ldc_I4_5:
            case Code.Ldc_I4_6:
            case Code.Ldc_I4_7:
            case Code.Ldc_I4_8:
                _stack.Push(new CppExpression { Code = ((int)instruction.OpCode.Code - (int)Code.Ldc_I4_0).ToString(CultureInfo.InvariantCulture), Type = _method.Module.TypeSystem.Int32 });
                return;
            case Code.Ldc_I4:
            case Code.Ldc_I4_S:
                _stack.Push(new CppExpression { Code = Convert.ToInt32(instruction.Operand, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), Type = _method.Module.TypeSystem.Int32 });
                return;
            case Code.Ldc_I8:
                _stack.Push(new CppExpression { Code = Convert.ToInt64(instruction.Operand, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), Type = _method.Module.TypeSystem.Int64 });
                return;
            case Code.Ldc_R4:
                _stack.Push(new CppExpression { Code = Convert.ToSingle(instruction.Operand, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture), Type = _method.Module.TypeSystem.Single });
                return;
            case Code.Ldc_R8:
                _stack.Push(new CppExpression { Code = Convert.ToDouble(instruction.Operand, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture), Type = _method.Module.TypeSystem.Double });
                return;
            case Code.Ldstr:
                _stack.Push(new CppExpression { Code = CppLiteral.String((string)instruction.Operand), Type = _method.Module.TypeSystem.String });
                return;
            case Code.Ldnull:
                _stack.Push(new CppExpression { Code = "nullptr", Type = _method.Module.TypeSystem.Object });
                return;
            case Code.Ldtoken:
                EmitLoadToken(instruction.Operand);
                return;
            case Code.Ldfld:
                LoadField((FieldReference)instruction.Operand, isStatic: false, asAddress: false);
                return;
            case Code.Ldsfld:
                LoadField((FieldReference)instruction.Operand, isStatic: true, asAddress: false);
                return;
            case Code.Ldflda:
                LoadField((FieldReference)instruction.Operand, isStatic: false, asAddress: true);
                return;
            case Code.Ldsflda:
                LoadField((FieldReference)instruction.Operand, isStatic: true, asAddress: true);
                return;
            case Code.Stfld:
                StoreField((FieldReference)instruction.Operand, isStatic: false, indentLevel);
                return;
            case Code.Stsfld:
                StoreField((FieldReference)instruction.Operand, isStatic: true, indentLevel);
                return;
            case Code.Newarr:
                EmitNewArray((TypeReference)instruction.Operand);
                return;
            case Code.Ldlen:
                EmitArrayLength();
                return;
            case Code.Ldelema:
                EmitLoadElementAddress((TypeReference)instruction.Operand);
                return;
            case Code.Ldelem_Any:
                EmitLoadElement((TypeReference)instruction.Operand, instruction.OpCode.Code);
                return;
            case Code.Ldelem_Ref:
            case Code.Ldelem_I:
            case Code.Ldelem_I1:
            case Code.Ldelem_I2:
            case Code.Ldelem_I4:
            case Code.Ldelem_I8:
            case Code.Ldelem_R4:
            case Code.Ldelem_R8:
            case Code.Ldelem_U1:
            case Code.Ldelem_U2:
            case Code.Ldelem_U4:
                EmitLoadElement(null, instruction.OpCode.Code);
                return;
            case Code.Ldind_I:
            case Code.Ldind_I1:
            case Code.Ldind_I2:
            case Code.Ldind_I4:
            case Code.Ldind_I8:
            case Code.Ldind_R4:
            case Code.Ldind_R8:
            case Code.Ldind_U1:
            case Code.Ldind_U2:
            case Code.Ldind_U4:
            case Code.Ldind_Ref:
                EmitLoadIndirect(null, instruction.OpCode.Code);
                return;
            case Code.Ldobj:
                EmitLoadIndirect((TypeReference)instruction.Operand, instruction.OpCode.Code);
                return;
            case Code.Stelem_Any:
                EmitStoreElement((TypeReference)instruction.Operand, instruction.OpCode.Code, indentLevel);
                return;
            case Code.Stelem_Ref:
            case Code.Stelem_I:
            case Code.Stelem_I1:
            case Code.Stelem_I2:
            case Code.Stelem_I4:
            case Code.Stelem_I8:
            case Code.Stelem_R4:
            case Code.Stelem_R8:
                EmitStoreElement(null, instruction.OpCode.Code, indentLevel);
                return;
            case Code.Stind_I:
            case Code.Stind_I1:
            case Code.Stind_I2:
            case Code.Stind_I4:
            case Code.Stind_I8:
            case Code.Stind_R4:
            case Code.Stind_R8:
            case Code.Stind_Ref:
                EmitStoreIndirect(null, instruction.OpCode.Code, indentLevel);
                return;
            case Code.Stobj:
                EmitStoreIndirect((TypeReference)instruction.Operand, instruction.OpCode.Code, indentLevel);
                return;
            case Code.Call:
            case Code.Callvirt:
                EmitCall((MethodReference)instruction.Operand, indentLevel);
                return;
            case Code.Newobj:
                EmitNewObject((MethodReference)instruction.Operand);
                return;
            case Code.Switch:
                return;
            case Code.Brfalse:
            case Code.Brfalse_S:
            case Code.Brtrue:
            case Code.Brtrue_S:
            case Code.Beq:
            case Code.Beq_S:
            case Code.Bne_Un:
            case Code.Bne_Un_S:
            case Code.Bge:
            case Code.Bge_S:
            case Code.Bge_Un:
            case Code.Bge_Un_S:
            case Code.Bgt:
            case Code.Bgt_S:
            case Code.Bgt_Un:
            case Code.Bgt_Un_S:
            case Code.Ble:
            case Code.Ble_S:
            case Code.Ble_Un:
            case Code.Ble_Un_S:
            case Code.Blt:
            case Code.Blt_S:
            case Code.Blt_Un:
            case Code.Blt_Un_S:
            case Code.Br:
            case Code.Br_S:
            case Code.Leave:
            case Code.Leave_S:
                return;
            case Code.Ret:
                EmitReturn(indentLevel);
                return;
            case Code.Pop:
            {
                var value = Pop();
                if (value.HasSideEffects)
                    AppendLine(indentLevel, $"{value.Code};");
                return;
            }
            case Code.Dup:
            {
                var value = Pop();
                if (value.HasSideEffects)
                    value = MaterializeTemporary(value, indentLevel, "dupTemp");
                _stack.Push(value);
                _stack.Push(value);
                return;
            }
            case Code.Ceq:
                EmitComparison("==");
                return;
            case Code.Cgt:
                EmitComparison(">");
                return;
            case Code.Cgt_Un:
                EmitUnsignedGreaterThan();
                return;
            case Code.Clt:
                EmitComparison("<");
                return;
            case Code.Clt_Un:
                EmitComparison("<");
                return;
            case Code.Add:
            case Code.Add_Ovf:
            case Code.Add_Ovf_Un:
                EmitBinary("+");
                return;
            case Code.Sub:
            case Code.Sub_Ovf:
            case Code.Sub_Ovf_Un:
                EmitBinary("-");
                return;
            case Code.Mul:
            case Code.Mul_Ovf:
            case Code.Mul_Ovf_Un:
                EmitBinary("*");
                return;
            case Code.Div:
            case Code.Div_Un:
                EmitBinary("/");
                return;
            case Code.Rem:
            case Code.Rem_Un:
                EmitBinary("%");
                return;
            case Code.And:
                EmitBinary(IsBooleanBinary() ? "&&" : "&");
                return;
            case Code.Or:
                EmitBinary(IsBooleanBinary() ? "||" : "|");
                return;
            case Code.Xor:
                EmitBinary("^");
                return;
            case Code.Shl:
                EmitBinary("<<");
                return;
            case Code.Shr:
            case Code.Shr_Un:
                EmitBinary(">>");
                return;
            case Code.Neg:
                EmitUnary("-");
                return;
            case Code.Not:
                EmitUnary("~");
                return;
            case Code.Castclass:
            case Code.Isinst:
            case Code.Unbox_Any:
                EmitCast((TypeReference)instruction.Operand);
                return;
            case Code.Box:
                EmitBox((TypeReference)instruction.Operand);
                return;
            case Code.Initobj:
                EmitInitObject((TypeReference)instruction.Operand, indentLevel);
                return;
            case Code.Cpobj:
                EmitCopyObject((TypeReference)instruction.Operand, indentLevel);
                return;
            case Code.Initblk:
                EmitInitBlock(indentLevel);
                return;
            case Code.Cpblk:
                EmitCopyBlock(indentLevel);
                return;
            case Code.Sizeof:
                EmitSizeOf((TypeReference)instruction.Operand);
                return;
            case Code.Ckfinite:
                return;
            case Code.Localloc:
                EmitLocalAlloc();
                return;
            case Code.Throw:
                EmitThrow(indentLevel);
                return;
            case Code.Rethrow:
                AppendLine(indentLevel, "throw;");
                return;
            case Code.Volatile:
            case Code.Readonly:
            case Code.Constrained:
            case Code.Unaligned:
            case Code.Tail:
                return;
            case Code.Endfinally:
            case Code.Endfilter:
                return;
            case Code.Conv_I1:
            case Code.Conv_I2:
            case Code.Conv_I4:
            case Code.Conv_I:
            case Code.Conv_I8:
            case Code.Conv_U1:
            case Code.Conv_U2:
            case Code.Conv_U4:
            case Code.Conv_U:
            case Code.Conv_U8:
            case Code.Conv_R4:
            case Code.Conv_R8:
            case Code.Conv_R_Un:
            case Code.Conv_Ovf_I:
            case Code.Conv_Ovf_I_Un:
            case Code.Conv_Ovf_I1:
            case Code.Conv_Ovf_I1_Un:
            case Code.Conv_Ovf_I2:
            case Code.Conv_Ovf_I2_Un:
            case Code.Conv_Ovf_I4:
            case Code.Conv_Ovf_I4_Un:
            case Code.Conv_Ovf_I8:
            case Code.Conv_Ovf_I8_Un:
            case Code.Conv_Ovf_U:
            case Code.Conv_Ovf_U_Un:
            case Code.Conv_Ovf_U1:
            case Code.Conv_Ovf_U1_Un:
            case Code.Conv_Ovf_U2:
            case Code.Conv_Ovf_U2_Un:
            case Code.Conv_Ovf_U4:
            case Code.Conv_Ovf_U4_Un:
            case Code.Conv_Ovf_U8:
            case Code.Conv_Ovf_U8_Un:
                EmitConversion(instruction.OpCode.Code);
                return;
            default:
                throw new NotSupportedException($"Unsupported IL opcode {instruction.OpCode.Code} in {_method.FullName}");
        }
    }
}
