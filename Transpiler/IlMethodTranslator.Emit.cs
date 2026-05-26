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
            AppendLine(indentLevel, BuildConsoleLogStatement(args));
            return;
        }

        if (method.DeclaringType.FullName == "System.Type" && method.Name == "GetTypeFromHandle" && args.Count == 1)
        {
            _stack.Push(
                new CppExpression
                {
                    Code = args[0].Code,
                    Type = method.ReturnType,
                    TypeToken = args[0].TypeToken,
                    PreferAutoDeclaration = true,
                }
            );
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
                Type = ResolveEffectiveReturnType(method, instance),
                PreferAutoDeclaration = true,
                HasSideEffects = true,
            };
        }

        if (method.Name == "LoadAsset" && instance != null && args.Count == 2 && args[1].TypeToken != null)
        {
            var typeArgument = args[1].TypeToken!;
            RequiredInclude(typeArgument);
            return new CppExpression
            {
                Code = $"{instance.Code}{GetMemberAccessOperator(instance.Type)}LoadAsset<{_typeSystem.MapType(typeArgument)}>({args[0].Code})",
                Type = typeArgument,
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
                Type = ResolveEffectiveReturnType(method, instance),
                PreferAutoDeclaration = method.Name.StartsWith("get_", StringComparison.Ordinal),
                HasSideEffects = true,
            };
        }

        if (instance != null)
        {
            return new CppExpression
            {
                Code = $"{instance.Code}{GetMemberAccessOperator(instance.Type)}{emittedMethodName}({argumentList})",
                Type = ResolveEffectiveReturnType(method, instance),
                PreferAutoDeclaration = true,
                HasSideEffects = true,
            };
        }

        return new CppExpression
        {
            Code = $"{declaringType}::{emittedMethodName}({argumentList})",
            Type = ResolveEffectiveReturnType(method, instance),
            PreferAutoDeclaration = method is GenericInstanceMethod,
            HasSideEffects = true,
        };
    }

    private static string BuildConsoleLogStatement(IReadOnlyList<CppExpression> args)
    {
        if (args.Count == 0)
            return "PaperLogger.info(\"\");";

        if (args.Count == 1 && TryUnwrapNewStringLiteral(args[0].Code, out var message))
            return $"PaperLogger.info({message});";

        return $"PaperLogger.info(\"{{}}\", {string.Join(", ", args.Select(arg => arg.Code))});";
    }

    private static bool TryUnwrapNewStringLiteral(string expression, out string cStringLiteral)
    {
        const string prefix = "il2cpp_utils::newcsstr(";
        cStringLiteral = "";

        var trimmedExpression = expression.Trim();
        if (!trimmedExpression.StartsWith(prefix, StringComparison.Ordinal) || !trimmedExpression.EndsWith(")", StringComparison.Ordinal))
            return false;

        cStringLiteral = trimmedExpression[prefix.Length..^1];
        return cStringLiteral.StartsWith("\"", StringComparison.Ordinal) && cStringLiteral.EndsWith("\"", StringComparison.Ordinal);
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
        if (method is GenericInstanceMethod genericMethod)
            return ResolveGenericReturnType(genericMethod);

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

    private static TypeReference ResolveGenericReturnType(GenericInstanceMethod method)
    {
        return SubstituteGenericReturnType(method.ReturnType, method);
    }

    private static TypeReference SubstituteGenericReturnType(TypeReference type, GenericInstanceMethod method)
    {
        if (type is GenericParameter genericParameter && genericParameter.Type == GenericParameterType.Method && genericParameter.Position >= 0 && genericParameter.Position < method.GenericArguments.Count)
            return method.GenericArguments[genericParameter.Position];

        if (type is ArrayType arrayType)
            return new ArrayType(SubstituteGenericReturnType(arrayType.ElementType, method), arrayType.Rank);

        if (type is ByReferenceType byReferenceType)
            return new ByReferenceType(SubstituteGenericReturnType(byReferenceType.ElementType, method));

        if (type is PointerType pointerType)
            return new PointerType(SubstituteGenericReturnType(pointerType.ElementType, method));

        return type;
    }
}
