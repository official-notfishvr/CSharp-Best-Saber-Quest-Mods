using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed partial class IlMethodTranslator
{
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

    private void EmitLoadToken(object? operand)
    {
        if (operand is TypeReference typeReference)
        {
            RequiredInclude(typeReference);
            var runtimeNamespace = typeReference.Namespace ?? "";
            var runtimeClassName = GetRuntimeClassName(typeReference);
            _stack.Push(
                new CppExpression
                {
                    Code = $"reinterpret_cast<::System::Type*>(::il2cpp_utils::GetSystemType(\"{runtimeNamespace}\", \"{runtimeClassName}\"))",
                    Type = _method.Module.ImportReference(typeof(Type)),
                    TypeToken = typeReference,
                    PreferAutoDeclaration = true,
                }
            );
            return;
        }

        throw new NotSupportedException($"Unsupported ldtoken operand {operand?.GetType().FullName ?? "<null>"} in {_method.FullName}");
    }

    private void EmitUnsignedGreaterThan()
    {
        var right = Pop();
        var left = Pop();
        if (IsNullLiteral(left) ^ IsNullLiteral(right))
        {
            var candidate = IsNullLiteral(left) ? right : left;
            _stack.Push(new CppExpression { Code = MakeExplicitBooleanCheck(candidate, invert: false), Type = _method.Module.TypeSystem.Boolean });
            return;
        }

        _stack.Push(new CppExpression { Code = $"({left.Code} > {right.Code})", Type = _method.Module.TypeSystem.Boolean });
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

    private void EmitCopyObject(TypeReference targetType, int indentLevel)
    {
        RequiredInclude(targetType);
        var source = Pop();
        var destination = Pop();
        AppendLine(indentLevel, $"{BuildIndirectAccess(destination)} = {BuildIndirectAccess(source)};");
    }

    private void EmitInitBlock(int indentLevel)
    {
        var size = Pop();
        var value = Pop();
        var destination = Pop();
        AppendLine(indentLevel, $"__builtin_memset({destination.Code}, {value.Code}, {size.Code});");
    }

    private void EmitCopyBlock(int indentLevel)
    {
        var size = Pop();
        var source = Pop();
        var destination = Pop();
        AppendLine(indentLevel, $"__builtin_memcpy({destination.Code}, {source.Code}, {size.Code});");
    }

    private void EmitSizeOf(TypeReference type)
    {
        RequiredInclude(type);
        _stack.Push(new CppExpression { Code = $"sizeof({_typeSystem.MapType(type)})", Type = _method.Module.TypeSystem.Int32 });
    }

    private void EmitLocalAlloc()
    {
        var size = Pop();
        _stack.Push(
            new CppExpression
            {
                Code = $"__builtin_alloca({size.Code})",
                Type = new PointerType(_method.Module.TypeSystem.Byte),
                PreferAutoDeclaration = true,
                HasSideEffects = true,
            }
        );
    }

    private void EmitThrow(int indentLevel)
    {
        if (_stack.Count == 0)
        {
            AppendLine(indentLevel, "throw;");
            return;
        }

        AppendLine(indentLevel, $"throw {Pop().Code};");
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

    private string MakeExplicitBooleanCheck(CppExpression value, bool invert)
    {
        if (IsBooleanType(value.Type))
            return MakeExplicitBooleanCheck(value.Code, invert);

        if (IsNullLiteral(value))
            return invert ? "true" : "false";

        if (SupportsDirectNullComparison(value.Type))
            return invert ? $"({value.Code} == nullptr)" : $"({value.Code} != nullptr)";

        return MakeExplicitBooleanCheck(value.Code, invert);
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

    private static bool SupportsDirectNullComparison(TypeReference? type)
    {
        type = NormalizeTypeReference(type);
        if (type == null)
            return false;

        if (type is ArrayType)
            return false;

        if (type is PointerType or FunctionPointerType)
            return true;

        if (type.IsValueType || type.Resolve()?.IsValueType == true)
            return false;

        return true;
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

        if (TryBuildNullComparison(left, right, string.Equals(op, "==", StringComparison.Ordinal), out expression))
            return true;

        if (TryBuildBooleanLiteralComparison(left, right, string.Equals(op, "==", StringComparison.Ordinal), out expression))
            return true;

        return TryBuildBooleanLiteralComparison(right, left, string.Equals(op, "==", StringComparison.Ordinal), out expression);
    }

    private bool TryBuildNullComparison(CppExpression left, CppExpression right, bool equalsComparison, out string expression)
    {
        expression = "";
        if (IsNullLiteral(left) == IsNullLiteral(right))
            return false;

        var candidate = IsNullLiteral(left) ? right : left;
        expression = MakeExplicitBooleanCheck(candidate, invert: equalsComparison);
        return true;
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
        return normalizedExpression.StartsWith("!(", StringComparison.Ordinal) && normalizedExpression.EndsWith(")", StringComparison.Ordinal) ? normalizedExpression[2..^1] : $"!({normalizedExpression})";
    }
}
