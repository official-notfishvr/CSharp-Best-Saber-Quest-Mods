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
                PushArgument(((ParameterDefinition)instruction.Operand).Index);
                return;
            case Code.Ldarga:
            case Code.Ldarga_S:
                PushArgumentAddress(((ParameterDefinition)instruction.Operand).Index);
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
                StoreArgument(((ParameterDefinition)instruction.Operand).Index, indentLevel);
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
                if (_useGotoFlow)
                {
                    AppendSwitchGoto((Instruction[])instruction.Operand, indentLevel);
                    return;
                }
                break;
            case Code.Brfalse:
            case Code.Brfalse_S:
                if (_useGotoFlow)
                {
                    AppendConditionalGoto((Instruction)instruction.Operand, MakeExplicitBooleanCheck(Pop().Code, invert: true), indentLevel);
                    return;
                }
                break;
            case Code.Brtrue:
            case Code.Brtrue_S:
                if (_useGotoFlow)
                {
                    AppendConditionalGoto((Instruction)instruction.Operand, MakeExplicitBooleanCheck(Pop().Code, invert: false), indentLevel);
                    return;
                }
                break;
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
                if (_useGotoFlow)
                {
                    AppendCompareGoto(instruction.OpCode.Code, (Instruction)instruction.Operand, indentLevel);
                    return;
                }
                break;
            case Code.Br:
            case Code.Br_S:
            case Code.Leave:
            case Code.Leave_S:
            {
                var target = (Instruction)instruction.Operand;
                if (_useGotoFlow)
                {
                    if (_gotoLabels != null && _instructionIndices.TryGetValue(target, out var gotoTargetIndex) && _gotoLabels.TryGetValue(gotoTargetIndex, out var gotoLabel))
                    {
                        AppendLine(indentLevel, $"goto {gotoLabel};");
                        return;
                    }
                }

                if (_instructionIndices.TryGetValue(target, out var branchTargetIndex))
                {
                    if (TryGetContinueLabel(branchTargetIndex, out var continueLabel))
                    {
                        AppendLine(indentLevel, $"goto {continueLabel};");
                        return;
                    }

                    if (IsLoopBreakTarget(branchTargetIndex))
                    {
                        AppendLine(indentLevel, "break;");
                        return;
                    }
                }

                if (TryBuildReturnFromBranchTarget(target, out var returnExpression))
                {
                    AppendLine(indentLevel, $"return {returnExpression};");
                    return;
                }

                if (_instructionIndices.TryGetValue(target, out var targetIndex) && targetIndex == _instructions.Count - 1 && _instructions[targetIndex].OpCode.Code == Code.Ret)
                    return;
                throw new NotSupportedException(
                    $"Unsupported non-structured branch in {_method.FullName} (gotoFlow={_useGotoFlow}, targetIndex={(_instructionIndices.TryGetValue(target, out var debugTargetIndex) ? debugTargetIndex : -1)}, hasLabel={(_gotoLabels != null && _instructionIndices.TryGetValue(target, out var labelTargetIndex) && _gotoLabels.ContainsKey(labelTargetIndex))})"
                );
            }
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

    private void PushArgument(int parameterIndex)
    {
        var parameter = _method.Parameters[parameterIndex];
        RequiredInclude(parameter.ParameterType);
        _stack.Push(new CppExpression { Code = GetArgumentName(parameterIndex), Type = parameter.ParameterType });
    }

    private void PushArgumentAddress(int parameterIndex)
    {
        var parameter = _method.Parameters[parameterIndex];
        RequiredInclude(parameter.ParameterType);
        _stack.Push(
            new CppExpression
            {
                Code = GetArgumentName(parameterIndex),
                Type = new ByReferenceType(parameter.ParameterType),
                PreferAutoDeclaration = true,
            }
        );
    }

    private void PushLocal(int index)
    {
        var variable = _method.Body!.Variables[index];
        RequiredInclude(variable.VariableType);
        _stack.Push(new CppExpression { Code = GetLocalName(index), Type = variable.VariableType });
    }

    private void PushLocalAddress(int index)
    {
        var variable = _method.Body!.Variables[index];
        RequiredInclude(variable.VariableType);
        _stack.Push(
            new CppExpression
            {
                Code = GetLocalName(index),
                Type = new ByReferenceType(variable.VariableType),
                PreferAutoDeclaration = true,
            }
        );
    }

    private void StoreLocal(int index, int indentLevel)
    {
        var variable = _method.Body!.Variables[index];
        var value = NormalizeAssignedValue(Pop(), variable.VariableType, GetLocalName(index));
        var name = GetLocalName(index);
        _recentLocalValues[index] = value;

        if (_declaredLocals.Add(index))
        {
            var declaredType = value.PreferAutoDeclaration ? "auto" : _typeSystem.MapType(variable.VariableType);
            AppendLine(indentLevel, $"{declaredType} {name} = {value.Code};");
        }
        else
        {
            AppendLine(indentLevel, $"{name} = {value.Code};");
        }
    }

    private void StoreArgument(int index, int indentLevel)
    {
        var value = Pop();
        var name = GetArgumentName(index);
        AppendLine(indentLevel, $"{name} = {value.Code};");
    }

    private void LoadField(FieldReference field, bool isStatic, bool asAddress)
    {
        RequiredInclude(field.FieldType);
        RequiredInclude(field.DeclaringType);

        if (isStatic && _configByField.TryGetValue(BuildConfigAccessorKey(field.DeclaringType.FullName, field.Name), out var config))
        {
            _stack.Push(new CppExpression { Code = config.CppIdentifier, Type = asAddress ? new ByReferenceType(config.Type) : config.Type });
            return;
        }

        if (isStatic && _localStaticFieldsByField.TryGetValue(BuildConfigAccessorKey(field.DeclaringType.FullName, field.Name), out var localStaticField))
        {
            _stack.Push(new CppExpression { Code = localStaticField.CppIdentifier, Type = asAddress ? new ByReferenceType(localStaticField.Type) : localStaticField.Type });
            return;
        }

        if (isStatic)
        {
            var declaringType = $"{_typeSystem.MapNamespace(field.DeclaringType.Namespace)}::{_typeSystem.ComposeTypeName(field.DeclaringType)}";
            _stack.Push(new CppExpression { Code = $"{declaringType}::{field.Name}", Type = asAddress ? new ByReferenceType(field.FieldType) : field.FieldType });
            return;
        }

        var target = Pop();
        var fieldName = ResolveInstanceFieldName(field);
        _stack.Push(
            new CppExpression
            {
                Code = $"{target.Code}{GetMemberAccessOperator(target.Type)}{fieldName}",
                Type = asAddress ? new ByReferenceType(field.FieldType) : field.FieldType,
                PreferAutoDeclaration = !asAddress,
            }
        );
    }

    private void StoreField(FieldReference field, bool isStatic, int indentLevel)
    {
        var value = Pop();

        if (isStatic && _configByField.TryGetValue(BuildConfigAccessorKey(field.DeclaringType.FullName, field.Name), out var config))
        {
            value = NormalizeAssignedValue(value, field.FieldType, config.CppIdentifier);
            AppendLine(indentLevel, $"{config.CppIdentifier} = {value.Code};");
            return;
        }

        if (isStatic && _localStaticFieldsByField.TryGetValue(BuildConfigAccessorKey(field.DeclaringType.FullName, field.Name), out var localStaticField))
        {
            value = NormalizeAssignedValue(value, field.FieldType, localStaticField.CppIdentifier);
            AppendLine(indentLevel, $"{localStaticField.CppIdentifier} = {value.Code};");
            return;
        }

        if (isStatic)
        {
            var declaringType = $"{_typeSystem.MapNamespace(field.DeclaringType.Namespace)}::{_typeSystem.ComposeTypeName(field.DeclaringType)}";
            value = NormalizeAssignedValue(value, field.FieldType, $"{declaringType}::{field.Name}");
            AppendLine(indentLevel, $"{declaringType}::{field.Name} = {value.Code};");
            return;
        }

        var target = Pop();
        var fieldName = ResolveInstanceFieldName(field);
        value = NormalizeAssignedValue(value, field.FieldType, $"{target.Code}{GetMemberAccessOperator(target.Type)}{fieldName}");
        AppendLine(indentLevel, $"{target.Code}{GetMemberAccessOperator(target.Type)}{fieldName} = {value.Code};");
    }

    private void EmitNewArray(TypeReference elementType)
    {
        var length = Pop();
        RequiredInclude(elementType);
        var elementCppType = _typeSystem.MapArrayElementTypeName(elementType);
        var arrayType = new ArrayType(elementType);

        _stack.Push(
            new CppExpression
            {
                Code = $"ArrayW<{elementCppType}>({length.Code})",
                Type = arrayType,
                PreferAutoDeclaration = true,
                HasSideEffects = true,
            }
        );
    }

    private void EmitArrayLength()
    {
        var array = Pop();
        _stack.Push(
            new CppExpression
            {
                Code = $"static_cast<int>({array.Code}.size())",
                Type = _method.Module.TypeSystem.Int32,
                PreferAutoDeclaration = true,
            }
        );
    }

    private void EmitLoadElementAddress(TypeReference elementType)
    {
        var index = Pop();
        var array = Pop();
        RequiredInclude(elementType);

        _stack.Push(
            new CppExpression
            {
                Code = $"{array.Code}[{index.Code}]",
                Type = new ByReferenceType(elementType),
                PreferAutoDeclaration = true,
            }
        );
    }

    private void EmitLoadElement(TypeReference? elementType, Code opcode)
    {
        var index = Pop();
        var array = Pop();
        var resolvedElementType = elementType ?? ResolveElementTypeFromOpcode(opcode);
        if (resolvedElementType != null)
            RequiredInclude(resolvedElementType);

        _stack.Push(
            new CppExpression
            {
                Code = $"{array.Code}[{index.Code}]",
                Type = resolvedElementType,
                PreferAutoDeclaration = true,
            }
        );
    }

    private void EmitStoreElement(TypeReference? elementType, Code opcode, int indentLevel)
    {
        var value = Pop();
        var index = Pop();
        var array = Pop();
        var resolvedElementType = elementType ?? ResolveElementTypeFromOpcode(opcode);
        if (resolvedElementType != null)
            RequiredInclude(resolvedElementType);

        AppendLine(indentLevel, $"{array.Code}[{index.Code}] = {value.Code};");
    }

    private void EmitLoadIndirect(TypeReference? elementType, Code opcode)
    {
        var address = Pop();
        var resolvedElementType = elementType ?? ResolveIndirectTypeFromOpcode(opcode, address.Type);
        if (resolvedElementType != null)
            RequiredInclude(resolvedElementType);

        _stack.Push(
            new CppExpression
            {
                Code = BuildIndirectAccess(address),
                Type = resolvedElementType,
                PreferAutoDeclaration = true,
            }
        );
    }

    private void EmitStoreIndirect(TypeReference? elementType, Code opcode, int indentLevel)
    {
        var value = Pop();
        var address = Pop();
        var resolvedElementType = elementType ?? ResolveIndirectTypeFromOpcode(opcode, address.Type);
        if (resolvedElementType != null)
            RequiredInclude(resolvedElementType);

        AppendLine(indentLevel, $"{BuildIndirectAccess(address)} = {value.Code};");
    }

    private TypeReference? ResolveElementTypeFromOpcode(Code opcode)
    {
        var types = _method.Module.TypeSystem;
        return opcode switch
        {
            Code.Ldelem_I1 or Code.Stelem_I1 => types.SByte,
            Code.Ldelem_U1 => types.Byte,
            Code.Ldelem_I2 or Code.Stelem_I2 => types.Int16,
            Code.Ldelem_U2 => types.UInt16,
            Code.Ldelem_I4 or Code.Stelem_I4 => types.Int32,
            Code.Ldelem_U4 => types.UInt32,
            Code.Ldelem_I8 or Code.Stelem_I8 => types.Int64,
            Code.Ldelem_I or Code.Stelem_I => types.IntPtr,
            Code.Ldelem_R4 or Code.Stelem_R4 => types.Single,
            Code.Ldelem_R8 or Code.Stelem_R8 => types.Double,
            Code.Ldelem_Ref or Code.Stelem_Ref => types.Object,
            _ => null,
        };
    }

    private TypeReference? ResolveIndirectTypeFromOpcode(Code opcode, TypeReference? addressType)
    {
        if (addressType is ByReferenceType byReferenceType)
            return byReferenceType.ElementType;

        if (addressType is PointerType pointerType)
            return pointerType.ElementType;

        var types = _method.Module.TypeSystem;
        return opcode switch
        {
            Code.Ldind_I1 or Code.Stind_I1 => types.SByte,
            Code.Ldind_U1 => types.Byte,
            Code.Ldind_I2 or Code.Stind_I2 => types.Int16,
            Code.Ldind_U2 => types.UInt16,
            Code.Ldind_I4 or Code.Stind_I4 => types.Int32,
            Code.Ldind_U4 => types.UInt32,
            Code.Ldind_I8 or Code.Stind_I8 => types.Int64,
            Code.Ldind_I or Code.Stind_I => types.IntPtr,
            Code.Ldind_R4 or Code.Stind_R4 => types.Single,
            Code.Ldind_R8 or Code.Stind_R8 => types.Double,
            Code.Ldind_Ref or Code.Stind_Ref => types.Object,
            _ => null,
        };
    }

    private static string BuildIndirectAccess(CppExpression address)
    {
        return address.Type is ByReferenceType ? address.Code : $"*({address.Code})";
    }

    private void EmitCall(MethodReference method, int indentLevel)
    {
        var args = new List<CppExpression>(method.Parameters.Count);
        for (var i = 0; i < method.Parameters.Count; i++)
            args.Insert(0, Pop());

        CppExpression? instance = null;
        if (method.HasThis)
            instance = Pop();

        foreach (var argument in args)
            RequiredInclude(argument.Type);

        if (IsConfigAccessor(method, out var configAccessor))
        {
            if (method.Name.StartsWith("get_", StringComparison.Ordinal))
            {
                _stack.Push(new CppExpression { Code = configAccessor.CppIdentifier, Type = configAccessor.Type });
                return;
            }

            AppendLine(indentLevel, $"{configAccessor.CppIdentifier} = {args[0].Code};");
            return;
        }

        if (method.DeclaringType.FullName == Hook.Method.DeclaringType.FullName && method.Name == Hook.Method.Name)
        {
            var originalCall = $"{Hook.HookName}({string.Join(", ", args.Select(arg => arg.Code))})";
            if (method.ReturnType.FullName == "System.Void")
                AppendLine(indentLevel, $"{originalCall};");
            else
                _stack.Push(
                    new CppExpression
                    {
                        Code = originalCall,
                        Type = method.ReturnType,
                        HasSideEffects = true,
                    }
                );
            return;
        }

        if (TryGetLocalMethodName(method, out var localMethodName))
        {
            var localCall = $"{localMethodName}({string.Join(", ", args.Select(arg => arg.Code))})";
            if (method.ReturnType.FullName == "System.Void")
                AppendLine(indentLevel, $"{localCall};");
            else
                _stack.Push(
                    new CppExpression
                    {
                        Code = localCall,
                        Type = method.ReturnType,
                        HasSideEffects = true,
                    }
                );
            return;
        }

        if (method.DeclaringType.FullName == "System.Console" && method.Name is "WriteLine" or "Write")
        {
            AppendLine(indentLevel, $"PaperLogger.info({string.Join(", ", args.Select(arg => arg.Code))});");
            return;
        }

        if (TryBuildSquaredMagnitudeIntrinsic(method, args, out var squaredMagnitudeExpression))
        {
            _stack.Push(
                new CppExpression
                {
                    Code = squaredMagnitudeExpression,
                    Type = method.ReturnType,
                    PreferAutoDeclaration = true,
                }
            );
            return;
        }

        RequiredInclude(method.ReturnType);
        if (!method.HasThis)
            RequiredInclude(method.DeclaringType);

        var callValue = BuildCallValue(method, instance, args);
        if (method.ReturnType.FullName == "System.Void")
        {
            AppendLine(indentLevel, $"{callValue.Code};");
            return;
        }

        _stack.Push(callValue);
    }

    private CppExpression BuildCallValue(MethodReference method, CppExpression? instance, IReadOnlyList<CppExpression> args)
    {
        var argumentList = string.Join(", ", args.Select(arg => arg.Code));
        var declaringType = $"{_typeSystem.MapNamespace(method.DeclaringType.Namespace)}::{_typeSystem.ComposeTypeName(method.DeclaringType)}";
        var emittedMethodName = ResolveMethodName(method);
        if (method is GenericInstanceMethod genericMethodNameSource && genericMethodNameSource.GenericArguments.Count > 0)
        {
            foreach (var genericArgument in genericMethodNameSource.GenericArguments)
                RequiredInclude(genericArgument);

            emittedMethodName += $"<{string.Join(", ", genericMethodNameSource.GenericArguments.Select(_typeSystem.MapType))}>";
        }

        if (method.Name == ".ctor" && instance != null)
        {
            return new CppExpression
            {
                Code = $"{instance.Code}{GetMemberAccessOperator(instance.Type)}_ctor({argumentList})",
                Type = method.ReturnType,
                HasSideEffects = true,
            };
        }

        if (method.Name is "GetComponentInChildren" or "GetComponent" && method is GenericInstanceMethod genericMethod && instance != null)
        {
            var typeArgument = genericMethod.GenericArguments[0];
            RequiredInclude(typeArgument);
            return new CppExpression
            {
                Code = $"{instance.Code}{GetMemberAccessOperator(instance.Type)}{method.Name}<{_typeSystem.MapType(typeArgument)}>({argumentList})",
                Type = method.ReturnType,
                PreferAutoDeclaration = true,
                HasSideEffects = true,
            };
        }

        if (TryGetLocalMethodName(method, out var localMethodName))
        {
            var localArgs = new List<string>();
            if (instance != null)
                localArgs.Add(instance.Code);
            localArgs.AddRange(args.Select(arg => arg.Code));
            return new CppExpression
            {
                Code = $"{localMethodName}({string.Join(", ", localArgs)})",
                Type = ResolveEffectiveReturnType(method, instance),
                PreferAutoDeclaration = true,
                HasSideEffects = true,
            };
        }

        if (ShouldUseRuntimeMethodInvocation(method))
            return BuildRuntimeCallValue(method, instance, args, emittedMethodName);

        if (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name.StartsWith("set_", StringComparison.Ordinal))
        {
            return new CppExpression
            {
                Code = instance != null ? $"{instance.Code}{GetMemberAccessOperator(instance.Type)}{emittedMethodName}({argumentList})" : $"{declaringType}::{emittedMethodName}({argumentList})",
                Type = method.ReturnType,
                PreferAutoDeclaration = method.Name.StartsWith("get_", StringComparison.Ordinal),
                HasSideEffects = true,
            };
        }

        if (instance != null)
        {
            return new CppExpression
            {
                Code = $"{instance.Code}{GetMemberAccessOperator(instance.Type)}{emittedMethodName}({argumentList})",
                Type = method.ReturnType,
                PreferAutoDeclaration = true,
                HasSideEffects = true,
            };
        }

        return new CppExpression
        {
            Code = $"{declaringType}::{emittedMethodName}({argumentList})",
            Type = method.ReturnType,
            HasSideEffects = true,
        };
    }

    private CppExpression BuildRuntimeCallValue(MethodReference method, CppExpression? instance, IReadOnlyList<CppExpression> args, string emittedMethodName)
    {
        var effectiveReturnType = ResolveEffectiveReturnType(method, instance);
        var mappedReturnType = _typeSystem.MapType(effectiveReturnType);
        var argumentList = string.Join(", ", args.Select(arg => arg.Code));
        string code;

        if (instance != null)
        {
            code = args.Count == 0 ? $"::il2cpp_utils::RunMethodRethrow<{mappedReturnType}, false>({instance.Code}, \"{emittedMethodName}\")" : $"::il2cpp_utils::RunMethodRethrow<{mappedReturnType}, false>({instance.Code}, \"{emittedMethodName}\", {argumentList})";
        }
        else
        {
            var runtimeNamespace = method.DeclaringType.Namespace ?? "";
            var runtimeClassName = GetRuntimeClassName(method.DeclaringType);
            var classExpression = $"::il2cpp_utils::GetClassFromName(\"{runtimeNamespace}\", \"{runtimeClassName}\")";
            code = args.Count == 0 ? $"::il2cpp_utils::RunMethodRethrow<{mappedReturnType}, false>({classExpression}, \"{emittedMethodName}\")" : $"::il2cpp_utils::RunMethodRethrow<{mappedReturnType}, false>({classExpression}, \"{emittedMethodName}\", {argumentList})";
        }

        return new CppExpression
        {
            Code = code,
            Type = effectiveReturnType,
            PreferAutoDeclaration = true,
            HasSideEffects = true,
        };
    }

    private bool IsConfigAccessor(MethodReference method, out ConfigEntry config)
    {
        var declaringTypeFullName = method.DeclaringType.FullName;
        if (_configByGetter.TryGetValue(BuildConfigAccessorKey(declaringTypeFullName, method.Name), out config!))
            return true;

        return _configBySetter.TryGetValue(BuildConfigAccessorKey(declaringTypeFullName, method.Name), out config!);
    }

    private bool TryGetPropertyAccessorName(MethodReference method, out string propertyName)
    {
        propertyName = "";
        if (!method.Name.StartsWith("get_", StringComparison.Ordinal) && !method.Name.StartsWith("set_", StringComparison.Ordinal))
            return false;

        try
        {
            var resolvedMethod = method.Resolve();
            for (var declaringType = resolvedMethod?.DeclaringType ?? method.DeclaringType.Resolve(); declaringType != null; declaringType = declaringType.BaseType?.Resolve())
            {
                var property = declaringType.Properties.FirstOrDefault(prop => prop.GetMethod?.Name == method.Name || prop.SetMethod?.Name == method.Name);
                if (property != null)
                {
                    propertyName = property.Name;
                    return true;
                }
            }
        }
        catch { }

        if (propertyName.Length > 0)
            return true;

        propertyName = _metadataIndex.ResolvePropertyName(method.DeclaringType.FullName, method.Name) ?? "";
        return propertyName.Length > 0;
    }

    private string ResolveMethodName(MethodReference method)
    {
        if ((method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name.StartsWith("set_", StringComparison.Ordinal)) && TryGetPropertyAccessorName(method, out var propertyName))
        {
            return $"{method.Name[..4]}{propertyName}";
        }

        return _metadataIndex.ResolveMethodName(method.DeclaringType.FullName, method.Name, method.Parameters.Count) ?? method.Name;
    }

    private bool ShouldUseRuntimeMethodInvocation(MethodReference method)
    {
        if (method.Name == ".ctor")
            return false;

        var declaringType = NormalizeTypeReference(method.DeclaringType);
        if (declaringType == null)
            return false;

        var namespaceName = declaringType.Namespace ?? "";
        if (!(namespaceName.Equals("System", StringComparison.Ordinal) || namespaceName.StartsWith("System.", StringComparison.Ordinal)))
            return false;

        return !method.HasThis || !IsValueType(declaringType);
    }

    private string GetRuntimeClassName(TypeReference type)
    {
        var names = new Stack<string>();
        TypeReference? current = type;
        while (current != null)
        {
            names.Push(current.Name.Split('`')[0]);
            current = current.DeclaringType;
        }

        return string.Join("/", names);
    }

    private TypeReference ResolveEffectiveReturnType(MethodReference method, CppExpression? instance)
    {
        if (method.Name == "get_Item")
        {
            var listInstance = NormalizeTypeReference(instance?.Type) as GenericInstanceType;
            if (listInstance != null && listInstance.GenericArguments.Count > 0)
                return listInstance.GenericArguments[0];
        }

        if (method.ReturnType is not GenericParameter genericParameter)
            return method.ReturnType;

        var genericInstance = NormalizeTypeReference(method.DeclaringType) as GenericInstanceType ?? NormalizeTypeReference(instance?.Type) as GenericInstanceType;
        if (genericInstance != null && genericParameter.Position >= 0 && genericParameter.Position < genericInstance.GenericArguments.Count)
            return genericInstance.GenericArguments[genericParameter.Position];

        return method.ReturnType;
    }

    private void EmitReturn(int indentLevel)
    {
        if (_method.ReturnType.FullName == "System.Void")
        {
            AppendLine(indentLevel, "return;");
            return;
        }

        AppendLine(indentLevel, $"return {Pop().Code};");
    }

    private void EmitBinary(string op)
    {
        var right = Pop();
        var left = Pop();
        _stack.Push(new CppExpression { Code = $"({left.Code} {op} {right.Code})", Type = left.Type ?? right.Type });
    }

    private bool IsBooleanBinary()
    {
        if (_stack.Count < 2)
            return false;

        var values = _stack.ToArray();
        return IsBooleanType(values[0].Type) && IsBooleanType(values[1].Type);
    }

    private void EmitComparison(string op)
    {
        var right = Pop();
        var left = Pop();
        var comparisonCode = TryBuildSimplifiedEqualityExpression(op, left, right, out var simplified) ? simplified : $"({left.Code} {op} {right.Code})";
        _stack.Push(new CppExpression { Code = comparisonCode, Type = _method.Module.TypeSystem.Boolean });
    }

    private void EmitUnsignedGreaterThan()
    {
        var right = Pop();
        var left = Pop();
        var op = IsNullLiteral(left) || IsNullLiteral(right) ? "!=" : ">";
        _stack.Push(new CppExpression { Code = $"({left.Code} {op} {right.Code})", Type = _method.Module.TypeSystem.Boolean });
    }

    private static bool IsNullLiteral(CppExpression value)
    {
        return string.Equals(value.Code, "nullptr", StringComparison.Ordinal);
    }

    private void EmitUnary(string op)
    {
        var operand = Pop();
        _stack.Push(new CppExpression { Code = $"({op}{operand.Code})", Type = operand.Type });
    }

    private void EmitConversion(Code opcode)
    {
        var operand = Pop();
        var targetType = opcode switch
        {
            Code.Conv_I or Code.Conv_Ovf_I or Code.Conv_Ovf_I_Un => "intptr_t",
            Code.Conv_I1 or Code.Conv_Ovf_I1 or Code.Conv_Ovf_I1_Un => "int8_t",
            Code.Conv_I2 or Code.Conv_Ovf_I2 or Code.Conv_Ovf_I2_Un => "int16_t",
            Code.Conv_I4 or Code.Conv_Ovf_I4 or Code.Conv_Ovf_I4_Un => "int32_t",
            Code.Conv_I8 or Code.Conv_Ovf_I8 or Code.Conv_Ovf_I8_Un => "int64_t",
            Code.Conv_U or Code.Conv_Ovf_U or Code.Conv_Ovf_U_Un => "uintptr_t",
            Code.Conv_U1 or Code.Conv_Ovf_U1 or Code.Conv_Ovf_U1_Un => "uint8_t",
            Code.Conv_U2 or Code.Conv_Ovf_U2 or Code.Conv_Ovf_U2_Un => "uint16_t",
            Code.Conv_U4 or Code.Conv_Ovf_U4 or Code.Conv_Ovf_U4_Un => "uint32_t",
            Code.Conv_U8 or Code.Conv_Ovf_U8 or Code.Conv_Ovf_U8_Un => "uint64_t",
            Code.Conv_R4 or Code.Conv_R_Un => "float",
            Code.Conv_R8 => "double",
            _ => throw new NotSupportedException($"Unsupported conversion opcode {opcode}"),
        };
        _stack.Push(new CppExpression { Code = $"static_cast<{targetType}>({operand.Code})", Type = ResolveConversionTypeReference(opcode) ?? operand.Type });
    }

    private TypeReference? ResolveConversionTypeReference(Code opcode)
    {
        var types = _method.Module.TypeSystem;
        return opcode switch
        {
            Code.Conv_I or Code.Conv_Ovf_I or Code.Conv_Ovf_I_Un => types.IntPtr,
            Code.Conv_I1 or Code.Conv_Ovf_I1 or Code.Conv_Ovf_I1_Un => types.SByte,
            Code.Conv_I2 or Code.Conv_Ovf_I2 or Code.Conv_Ovf_I2_Un => types.Int16,
            Code.Conv_I4 or Code.Conv_Ovf_I4 or Code.Conv_Ovf_I4_Un => types.Int32,
            Code.Conv_I8 or Code.Conv_Ovf_I8 or Code.Conv_Ovf_I8_Un => types.Int64,
            Code.Conv_U or Code.Conv_Ovf_U or Code.Conv_Ovf_U_Un => types.UIntPtr,
            Code.Conv_U1 or Code.Conv_Ovf_U1 or Code.Conv_Ovf_U1_Un => types.Byte,
            Code.Conv_U2 or Code.Conv_Ovf_U2 or Code.Conv_Ovf_U2_Un => types.UInt16,
            Code.Conv_U4 or Code.Conv_Ovf_U4 or Code.Conv_Ovf_U4_Un => types.UInt32,
            Code.Conv_U8 or Code.Conv_Ovf_U8 or Code.Conv_Ovf_U8_Un => types.UInt64,
            Code.Conv_R4 or Code.Conv_R_Un => types.Single,
            Code.Conv_R8 => types.Double,
            _ => null,
        };
    }

    private void EmitNewObject(MethodReference constructor)
    {
        var args = new List<CppExpression>(constructor.Parameters.Count);
        for (var i = 0; i < constructor.Parameters.Count; i++)
            args.Insert(0, Pop());

        RequiredInclude(constructor.DeclaringType);
        _stack.Push(BuildNewObjectValue(constructor, args));
    }

    private void EmitCast(TypeReference targetType)
    {
        var operand = Pop();
        RequiredInclude(targetType);
        _stack.Push(
            new CppExpression
            {
                Code = BuildCastExpression(targetType, operand.Code),
                Type = targetType,
                PreferAutoDeclaration = true,
            }
        );
    }

    private void EmitBox(TypeReference sourceType)
    {
        var operand = Pop();
        RequiredInclude(sourceType);
        _stack.Push(
            new CppExpression
            {
                Code = $"reinterpret_cast<Il2CppObject*>({operand.Code})",
                Type = _method.Module.TypeSystem.Object,
                PreferAutoDeclaration = true,
            }
        );
    }

    private void EmitInitObject(TypeReference targetType, int indentLevel)
    {
        RequiredInclude(targetType);
        AppendLine(indentLevel, $"{Pop().Code} = {{}};");
    }

    private CppExpression BuildNewObjectValue(MethodReference constructor, IReadOnlyList<CppExpression> args)
    {
        var declaringType = constructor.DeclaringType;
        var declaringTypeName = _typeSystem.MapType(declaringType).TrimEnd('*');
        var argumentList = string.Join(", ", args.Select(arg => arg.Code));

        if (declaringType.IsValueType || declaringType.Resolve()?.IsValueType == true)
        {
            var mappedType = _typeSystem.MapType(declaringType);
            var ctorBody = args.Count == 0 ? "return tmpValue;" : $"tmpValue._ctor({argumentList}); return tmpValue;";
            return new CppExpression
            {
                Code = $"[&]() {{ {mappedType} tmpValue{{}}; {ctorBody} }}()",
                Type = declaringType,
                PreferAutoDeclaration = true,
                HasSideEffects = true,
            };
        }

        return new CppExpression
        {
            Code = $"{declaringTypeName}::New_ctor({argumentList})",
            Type = declaringType,
            PreferAutoDeclaration = true,
            HasSideEffects = true,
        };
    }

    private string BuildCastExpression(TypeReference targetType, string operandCode)
    {
        var mappedType = _typeSystem.MapType(targetType);
        if (mappedType.EndsWith("*", StringComparison.Ordinal) && operandCode.Contains("GetComponent(", StringComparison.Ordinal))
            return $"reinterpret_cast<{mappedType}>(({operandCode}).ptr())";

        return mappedType.EndsWith("*", StringComparison.Ordinal) || mappedType.EndsWith("&", StringComparison.Ordinal) ? $"reinterpret_cast<{mappedType}>({operandCode})" : $"static_cast<{mappedType}>({operandCode})";
    }

    private string ResolveInstanceFieldName(FieldReference field)
    {
        return _metadataIndex.ResolveFieldStorageName(field.DeclaringType.FullName, field.Name) ?? field.Name;
    }

    private bool TryBuildSquaredMagnitudeIntrinsic(MethodReference method, IReadOnlyList<CppExpression> args, out string expression)
    {
        expression = "";
        if (method.HasThis || method.Name != "SqrMagnitude" || args.Count != 1)
            return false;

        var argumentType = NormalizeTypeReference(args[0].Type);
        var declaringType = NormalizeTypeReference(method.DeclaringType);
        if (argumentType == null || declaringType == null)
            return false;

        if (!string.Equals(argumentType.FullName, declaringType.FullName, StringComparison.Ordinal))
        {
            var argumentResolved = argumentType.Resolve();
            var declaringResolved = declaringType.Resolve();
            if (argumentResolved == null || declaringResolved == null || !string.Equals(argumentResolved.FullName, declaringResolved.FullName, StringComparison.Ordinal))
                return false;
        }

        var componentFields = ResolveVectorLikeComponentFields(argumentType, declaringType.FullName);
        if (componentFields.Count == 0)
            return false;

        var accessOperator = GetMemberAccessOperator(argumentType);
        var terms = componentFields.Select(fieldName =>
        {
            var storageName = _metadataIndex.ResolveFieldStorageName(method.DeclaringType.FullName, fieldName) ?? fieldName;
            return $"({args[0].Code}{accessOperator}{storageName} * {args[0].Code}{accessOperator}{storageName})";
        });

        expression = $"({string.Join(" + ", terms)})";
        return true;
    }

    private void AppendConditionalGoto(Instruction targetInstruction, string condition, int indentLevel)
    {
        if (_gotoLabels != null && _instructionIndices.TryGetValue(targetInstruction, out var targetIndex) && _gotoLabels.TryGetValue(targetIndex, out var label))
        {
            AppendLine(indentLevel, $"if ({condition}) goto {label};");
            return;
        }

        throw new NotSupportedException($"Unsupported goto branch target in {_method.FullName}");
    }

    private void AppendSwitchGoto(Instruction[] targets, int indentLevel)
    {
        var switchValue = Pop();
        AppendLine(indentLevel, $"switch ({switchValue.Code}) {{");

        if (_gotoLabels == null)
            throw new NotSupportedException($"Unsupported switch branch target in {_method.FullName}");

        for (var i = 0; i < targets.Length; i++)
        {
            if (!_instructionIndices.TryGetValue(targets[i], out var targetIndex) || !_gotoLabels.TryGetValue(targetIndex, out var label))
                throw new NotSupportedException($"Unsupported switch branch target in {_method.FullName}");

            AppendLine(indentLevel + 1, $"case {i}: goto {label};");
        }

        AppendLine(indentLevel + 1, "default: break;");
        AppendLine(indentLevel, "}");
    }

    private void AppendCompareGoto(Code opcode, Instruction targetInstruction, int indentLevel)
    {
        var right = Pop();
        var left = Pop();
        var condition = TryBuildSimplifiedCompareCondition(opcode, left, right, branchWhenTrue: true, out var simplified)
            ? simplified
            : opcode switch
            {
                Code.Beq or Code.Beq_S => $"({left.Code} == {right.Code})",
                Code.Bne_Un or Code.Bne_Un_S => $"({left.Code} != {right.Code})",
                Code.Bge or Code.Bge_S or Code.Bge_Un or Code.Bge_Un_S => $"({left.Code} >= {right.Code})",
                Code.Bgt or Code.Bgt_S or Code.Bgt_Un or Code.Bgt_Un_S => $"({left.Code} > {right.Code})",
                Code.Ble or Code.Ble_S or Code.Ble_Un or Code.Ble_Un_S => $"({left.Code} <= {right.Code})",
                Code.Blt or Code.Blt_S or Code.Blt_Un or Code.Blt_Un_S => $"({left.Code} < {right.Code})",
                _ => throw new NotSupportedException($"Unsupported compare branch opcode {opcode}"),
            };

        AppendConditionalGoto(targetInstruction, condition, indentLevel);
    }

    private bool TryGetContinueLabel(int targetIndex, out string label)
    {
        foreach (var scope in _continueLabelScopes)
        {
            if (scope.TryGetValue(targetIndex, out label!))
                return true;
        }

        label = "";
        return false;
    }

    private bool IsLoopBreakTarget(int targetIndex)
    {
        foreach (var scope in _breakTargetScopes)
        {
            if (scope.Contains(targetIndex))
                return true;
        }

        return false;
    }

    private static TypeReference? NormalizeTypeReference(TypeReference? type)
    {
        while (type is OptionalModifierType optionalModifierType)
            type = optionalModifierType.ElementType;

        while (type is RequiredModifierType requiredModifierType)
            type = requiredModifierType.ElementType;

        while (type is ByReferenceType byReferenceType)
            type = byReferenceType.ElementType;

        return type;
    }

    private bool TryGetLocalMethodName(MethodReference method, out string localMethodName)
    {
        var resolvedMethod = method.Resolve();
        if (resolvedMethod != null && _localMethodNames.TryGetValue(resolvedMethod.FullName, out localMethodName!))
            return true;

        return _localMethodNames.TryGetValue(method.FullName, out localMethodName!);
    }

    private IReadOnlyList<string> ResolveVectorLikeComponentFields(TypeReference argumentType, string declaringTypeFullName)
    {
        var metadataFields = _metadataIndex.ResolveSquaredMagnitudeComponentFields(declaringTypeFullName);
        if (metadataFields.Count > 0)
            return metadataFields;

        var resolvedType = argumentType.Resolve();
        if (resolvedType == null || !resolvedType.IsValueType)
            return Array.Empty<string>();

        var numericFields = new List<string>();
        foreach (var candidate in new[] { "x", "y", "z", "w" })
        {
            var field = resolvedType.Fields.FirstOrDefault(item => string.Equals(item.Name, candidate, StringComparison.Ordinal));
            if (field == null || field.IsStatic || !IsNumericMetadataType(field.FieldType.MetadataType))
                break;

            numericFields.Add(field.Name);
        }

        return numericFields.Count >= 2 ? numericFields : Array.Empty<string>();
    }

    private static bool IsNumericMetadataType(MetadataType metadataType)
    {
        return metadataType is MetadataType.Byte or MetadataType.SByte or MetadataType.Int16 or MetadataType.UInt16 or MetadataType.Int32 or MetadataType.UInt32 or MetadataType.Int64 or MetadataType.UInt64 or MetadataType.Single or MetadataType.Double;
    }

    private CppExpression MaterializeTemporary(CppExpression value, int indentLevel, string prefix)
    {
        var tempName = $"{prefix}{_temporaryCounter++}";
        var tempType = value.Type == null ? "Il2CppObject*" : _typeSystem.MapType(value.Type);
        var declaration = ShouldValueInitializeLocal(value.Type) ? $"{tempType} {tempName}{{}};" : $"{tempType} {tempName};";
        _temporaryDeclarations.Add(declaration);
        AppendLine(indentLevel, $"{tempName} = {value.Code};");
        return new CppExpression
        {
            Code = tempName,
            Type = value.Type,
            PreferAutoDeclaration = true,
        };
    }

    private CppExpression NormalizeAssignedValue(CppExpression value, TypeReference targetType, string targetExpression)
    {
        var mappedTargetType = _typeSystem.MapType(targetType);

        const string runtimePrefix = "::il2cpp_utils::RunMethodRethrow<Il2CppObject*, false>(";
        if (value.Code.StartsWith(runtimePrefix, StringComparison.Ordinal) && !string.Equals(mappedTargetType, "Il2CppObject*", StringComparison.Ordinal))
        {
            value = new CppExpression
            {
                Code = $"::il2cpp_utils::RunMethodRethrow<{mappedTargetType}, false>({value.Code[runtimePrefix.Length..]}",
                Type = targetType,
                PreferAutoDeclaration = value.PreferAutoDeclaration,
                HasSideEffects = value.HasSideEffects,
            };
        }

        var newCtorMarker = "::New_ctor(";
        var newCtorIndex = value.Code.IndexOf(newCtorMarker, StringComparison.Ordinal);
        if (newCtorIndex >= 0)
        {
            var args = value.Code[(newCtorIndex + newCtorMarker.Length)..^1].Trim();
            if (string.Equals(args, targetExpression, StringComparison.Ordinal))
                args = string.Empty;
            value = new CppExpression
            {
                Code = $"::il2cpp_utils::NewSpecific<decltype({targetExpression})>({args})",
                Type = targetType,
                PreferAutoDeclaration = value.PreferAutoDeclaration,
                HasSideEffects = value.HasSideEffects,
            };
        }

        const string newSpecificPrefix = "::il2cpp_utils::NewSpecific<";
        if (value.Code.StartsWith(newSpecificPrefix, StringComparison.Ordinal))
        {
            var callStart = value.Code.IndexOf('(', newSpecificPrefix.Length);
            if (callStart >= 0)
            {
                var args = value.Code[(callStart + 1)..^1].Trim();
                if (string.Equals(args, targetExpression, StringComparison.Ordinal))
                    args = string.Empty;
                value = new CppExpression
                {
                    Code = $"::il2cpp_utils::NewSpecific<decltype({targetExpression})>({args})",
                    Type = targetType,
                    PreferAutoDeclaration = value.PreferAutoDeclaration,
                    HasSideEffects = value.HasSideEffects,
                };
            }
        }

        return value;
    }

    private static string MakeExplicitBooleanCheck(string value, bool invert)
    {
        if (string.IsNullOrWhiteSpace(value))
            return invert ? "false" : "true";

        var normalizedValue = value.Trim();
        if (normalizedValue is "0" or "false" or "nullptr")
        {
            return invert ? "true" : "false";
        }

        if (normalizedValue is "1" or "true")
        {
            return invert ? "false" : "true";
        }

        return invert ? $"!({normalizedValue})" : $"({normalizedValue})";
    }

    private bool TryBuildSimplifiedCompareCondition(Code opcode, CppExpression left, CppExpression right, bool branchWhenTrue, out string condition)
    {
        condition = "";
        string? comparisonOperator = opcode switch
        {
            Code.Beq or Code.Beq_S => "==",
            Code.Bne_Un or Code.Bne_Un_S => "!=",
            _ => null,
        };

        if (comparisonOperator == null)
            return false;

        if (!TryBuildSimplifiedEqualityExpression(comparisonOperator, left, right, out var comparisonExpression))
            return false;

        condition = branchWhenTrue ? comparisonExpression : NegateBooleanExpression(comparisonExpression);
        return true;
    }

    private bool TryBuildSimplifiedEqualityExpression(string op, CppExpression left, CppExpression right, out string expression)
    {
        expression = "";
        if (!string.Equals(op, "==", StringComparison.Ordinal) && !string.Equals(op, "!=", StringComparison.Ordinal))
            return false;

        if (TryBuildBooleanLiteralComparison(left, right, string.Equals(op, "==", StringComparison.Ordinal), out expression))
            return true;

        return TryBuildBooleanLiteralComparison(right, left, string.Equals(op, "==", StringComparison.Ordinal), out expression);
    }

    private bool TryBuildBooleanLiteralComparison(CppExpression candidateBoolean, CppExpression candidateLiteral, bool equalsComparison, out string expression)
    {
        expression = "";
        if (!IsBooleanType(candidateBoolean.Type) || !TryGetBooleanLiteralValue(candidateLiteral.Code, out var literalValue))
            return false;

        var shouldBeTrue = equalsComparison ? literalValue : !literalValue;
        expression = shouldBeTrue ? $"({candidateBoolean.Code})" : NegateBooleanExpression(candidateBoolean.Code);
        return true;
    }

    private static bool TryGetBooleanLiteralValue(string code, out bool value)
    {
        switch (code.Trim())
        {
            case "0":
            case "false":
                value = false;
                return true;
            case "1":
            case "true":
                value = true;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static string NegateBooleanExpression(string expression)
    {
        var normalizedExpression = expression.Trim();
        return normalizedExpression.StartsWith("!(", StringComparison.Ordinal) && normalizedExpression.EndsWith(")", StringComparison.Ordinal)
            ? normalizedExpression[2..^1]
            : $"!({normalizedExpression})";
    }
}
