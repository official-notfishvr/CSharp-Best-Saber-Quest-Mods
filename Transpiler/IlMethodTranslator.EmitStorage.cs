using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed partial class IlMethodTranslator
{
    private void PushArgument(int parameterIndex)
    {
        if (_method.HasThis && parameterIndex == 0)
        {
            _stack.Push(new CppExpression { Code = "this", Type = _method.DeclaringType });
            return;
        }

        var parameter = _method.Parameters[_method.HasThis ? parameterIndex - 1 : parameterIndex];
        RequiredInclude(parameter.ParameterType);
        _stack.Push(new CppExpression { Code = GetArgumentName(_method.HasThis ? parameterIndex - 1 : parameterIndex), Type = parameter.ParameterType });
    }

    private void PushArgumentAddress(int parameterIndex)
    {
        if (_method.HasThis && parameterIndex == 0)
        {
            _stack.Push(
                new CppExpression
                {
                    Code = "this",
                    Type = new ByReferenceType(_method.DeclaringType),
                    PreferAutoDeclaration = true,
                }
            );
            return;
        }

        var parameter = _method.Parameters[_method.HasThis ? parameterIndex - 1 : parameterIndex];
        RequiredInclude(parameter.ParameterType);
        _stack.Push(
            new CppExpression
            {
                Code = GetArgumentName(_method.HasThis ? parameterIndex - 1 : parameterIndex),
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
        if (_method.HasThis && index == 0)
            throw new NotSupportedException($"Cannot assign to this in {_method.FullName}");

        var value = Pop();
        var name = GetArgumentName(_method.HasThis ? index - 1 : index);
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
}
