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
            case Code.Call:
            case Code.Callvirt:
                EmitCall((MethodReference)instruction.Operand, indentLevel);
                return;
            case Code.Newobj:
                EmitNewObject((MethodReference)instruction.Operand);
                return;
            case Code.Br:
            case Code.Br_S:
            case Code.Leave:
            case Code.Leave_S:
            {
                var target = (Instruction)instruction.Operand;
                if (TryBuildReturnFromBranchTarget(target, out var returnExpression))
                {
                    AppendLine(indentLevel, $"return {returnExpression};");
                    return;
                }

                if (_instructionIndices.TryGetValue(target, out var targetIndex) && targetIndex == _instructions.Count - 1 && _instructions[targetIndex].OpCode.Code == Code.Ret)
                    return;
                throw new NotSupportedException($"Unsupported non-structured branch in {_method.FullName}");
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
            case Code.Conv_I1:
            case Code.Conv_I2:
            case Code.Conv_I4:
            case Code.Conv_I8:
            case Code.Conv_U1:
            case Code.Conv_U2:
            case Code.Conv_U4:
            case Code.Conv_U8:
            case Code.Conv_R4:
            case Code.Conv_R8:
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
                Type = parameter.ParameterType,
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
                Type = variable.VariableType,
                PreferAutoDeclaration = true,
            }
        );
    }

    private void StoreLocal(int index, int indentLevel)
    {
        var value = Pop();
        var name = GetLocalName(index);
        var variable = _method.Body!.Variables[index];

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
            _stack.Push(new CppExpression { Code = config.CppIdentifier, Type = config.Type });
            return;
        }

        if (isStatic)
        {
            var declaringType = $"{_typeSystem.MapNamespace(field.DeclaringType.Namespace)}::{_typeSystem.ComposeTypeName(field.DeclaringType)}";
            _stack.Push(new CppExpression { Code = $"{declaringType}::{field.Name}", Type = field.FieldType });
            return;
        }

        var target = Pop();
        _stack.Push(
            new CppExpression
            {
                Code = $"{target.Code}{GetMemberAccessOperator(target.Type)}{field.Name}",
                Type = field.FieldType,
                PreferAutoDeclaration = !asAddress,
            }
        );
    }

    private void StoreField(FieldReference field, bool isStatic, int indentLevel)
    {
        var value = Pop();

        if (isStatic && _configByField.TryGetValue(BuildConfigAccessorKey(field.DeclaringType.FullName, field.Name), out var config))
        {
            AppendLine(indentLevel, $"{config.CppIdentifier} = {value.Code};");
            return;
        }

        if (isStatic)
        {
            var declaringType = $"{_typeSystem.MapNamespace(field.DeclaringType.Namespace)}::{_typeSystem.ComposeTypeName(field.DeclaringType)}";
            AppendLine(indentLevel, $"{declaringType}::{field.Name} = {value.Code};");
            return;
        }

        var target = Pop();
        AppendLine(indentLevel, $"{target.Code}{GetMemberAccessOperator(target.Type)}{field.Name} = {value.Code};");
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
                Code = $"&({array.Code}[{index.Code}])",
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

        if (method.DeclaringType.FullName == "System.Console" && method.Name is "WriteLine" or "Write")
        {
            AppendLine(indentLevel, $"PaperLogger.info({string.Join(", ", args.Select(arg => arg.Code))});");
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

        if (method.Name == ".ctor" && instance != null)
        {
            return new CppExpression
            {
                Code = $"{instance.Code}{GetMemberAccessOperator(instance.Type)}_ctor({argumentList})",
                Type = method.ReturnType,
                HasSideEffects = true,
            };
        }

        if (method.Name.StartsWith("get_", StringComparison.Ordinal) && TryGetPropertyAccessorName(method, out var propertyName) && instance != null)
        {
            return new CppExpression
            {
                Code = $"{instance.Code}{GetMemberAccessOperator(instance.Type)}{propertyName}",
                Type = method.ReturnType,
                PreferAutoDeclaration = true,
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

        if (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name.StartsWith("set_", StringComparison.Ordinal))
        {
            if (TryGetPropertyAccessorName(method, out var metadataPropertyName) && instance != null)
            {
                if (method.Name.StartsWith("get_", StringComparison.Ordinal))
                {
                    return new CppExpression
                    {
                        Code = instance != null ? $"{instance.Code}{GetMemberAccessOperator(instance.Type)}{metadataPropertyName}" : $"{declaringType}::{metadataPropertyName}",
                        Type = method.ReturnType,
                        PreferAutoDeclaration = true,
                    };
                }

                return new CppExpression
                {
                    Code = instance != null ? $"{instance.Code}{GetMemberAccessOperator(instance.Type)}{metadataPropertyName} = {argumentList}" : $"{declaringType}::{metadataPropertyName} = {argumentList}",
                    Type = method.ReturnType,
                    HasSideEffects = true,
                };
            }

            var accessorName = NormalizeAccessorName(method.Name);
            return new CppExpression
            {
                Code = instance != null ? $"{instance.Code}{GetMemberAccessOperator(instance.Type)}{accessorName}({argumentList})" : $"{declaringType}::{accessorName}({argumentList})",
                Type = method.ReturnType,
                PreferAutoDeclaration = method.Name.StartsWith("get_", StringComparison.Ordinal),
                HasSideEffects = true,
            };
        }

        if (instance != null)
        {
            return new CppExpression
            {
                Code = $"{instance.Code}{GetMemberAccessOperator(instance.Type)}{method.Name}({argumentList})",
                Type = method.ReturnType,
                PreferAutoDeclaration = true,
                HasSideEffects = true,
            };
        }

        return new CppExpression
        {
            Code = $"{declaringType}::{method.Name}({argumentList})",
            Type = method.ReturnType,
            HasSideEffects = true,
        };
    }

    private static string NormalizeAccessorName(string methodName)
    {
        var prefix = methodName[..4];
        var suffix = methodName[4..];
        if (string.IsNullOrEmpty(suffix))
            return methodName;

        return prefix + char.ToLowerInvariant(suffix[0]) + suffix[1..];
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
        _stack.Push(new CppExpression { Code = $"({left.Code} {op} {right.Code})", Type = _method.Module.TypeSystem.Boolean });
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
            Code.Conv_I1 => "int8_t",
            Code.Conv_I2 => "int16_t",
            Code.Conv_I4 => "int32_t",
            Code.Conv_I8 => "int64_t",
            Code.Conv_U1 => "uint8_t",
            Code.Conv_U2 => "uint16_t",
            Code.Conv_U4 => "uint32_t",
            Code.Conv_U8 => "uint64_t",
            Code.Conv_R4 => "float",
            Code.Conv_R8 => "double",
            _ => throw new NotSupportedException($"Unsupported conversion opcode {opcode}"),
        };
        _stack.Push(new CppExpression { Code = $"static_cast<{targetType}>({operand.Code})", Type = operand.Type });
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
        var declaringTypeName = $"{_typeSystem.MapNamespace(declaringType.Namespace)}::{_typeSystem.ComposeTypeName(declaringType)}";
        var argumentList = string.Join(", ", args.Select(arg => arg.Code));

        if (declaringType.IsValueType || declaringType.Resolve()?.IsValueType == true)
        {
            var mappedType = _typeSystem.MapType(declaringType);
            var ctorBody = args.Count == 0 ? "return value;" : $"value._ctor({argumentList}); return value;";
            return new CppExpression
            {
                Code = $"[&]() {{ {mappedType} value{{}}; {ctorBody} }}()",
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
        return mappedType.EndsWith("*", StringComparison.Ordinal) || mappedType.EndsWith("&", StringComparison.Ordinal) ? $"reinterpret_cast<{mappedType}>({operandCode})" : $"static_cast<{mappedType}>({operandCode})";
    }
}
