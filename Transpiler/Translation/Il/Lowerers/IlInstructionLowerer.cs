using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal static class IlInstructionLowerer
{
    public static bool TryLower(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack, out QuestLogStatement? statement)
    {
        statement = null;
        var code = instruction.OpCode.Code;
        if (code == Code.Nop)
            return true;
        if (code == Code.Ret)
        {
            if (context.EffectiveReturnType.MetadataType == MetadataType.Void)
            {
                RequireStack(context, instruction, stack, 0);
                statement = Raw(context.NativeReturnType == "void" ? "return;" : "return result;", "IL return");
            }
            else
            {
                var value = Pop(context, instruction, stack);
                var ilBooleanReturn = context.EffectiveReturnType.MetadataType == MetadataType.Boolean && value.PcType == "System.Int32";
                if ((!ilBooleanReturn && value.PcType != context.EffectiveReturnType.FullName) || value.IsByRef || context.NativeReturnType == "void")
                    throw Unsupported(context.Method, instruction, "return value does not match the translated method return type");
                statement = Raw($"return {value.Code};", "IL return", value.ApiMapping);
            }
            return true;
        }
        if (TryLoadArgument(context, instruction, stack) || TryLoadLocal(context, instruction, stack))
            return true;
        if (IlReflectionLowerer.TryLower(context, instruction, stack, out statement))
            return true;
        if (code == Code.Isinst && instruction.Operand is TypeReference instanceType)
        {
            var source = Pop(context, instruction, stack);
            var targetType = QuestApiTypeMapper.Map(instanceType);
            if (source.IsByRef || source.DeferredBoxedPrimitiveType is not null || instanceType.IsValueType || targetType is null || !targetType.EndsWith("*", StringComparison.Ordinal))
                throw Unsupported(context.Method, instruction, "isinst requires a reference source and a verified Quest reference type");
            var sourceType = context.Method.Module.GetType(source.PcType);
            var sourceNativeType = sourceType is null ? MapFullNameToNativeReference(source.PcType) : QuestApiTypeMapper.Map(sourceType);
            var pointerSource = sourceNativeType is not null && sourceNativeType.EndsWith("*", StringComparison.Ordinal) ? $"static_cast<{sourceNativeType}>({source.Code})" : source.Code;
            var boxedSource = $"reinterpret_cast<Il2CppObject*>({pointerSource})";
            var converted = $"reinterpret_cast<{targetType}>({pointerSource})";
            stack.Push(new QuestValue($"(({source.Code}) == nullptr || !il2cpp_functions::class_is_assignable_from(classof({targetType}), il2cpp_functions::object_get_class({boxedSource})) ? nullptr : {converted})", instanceType.FullName, false, source.ApiMapping));
            return true;
        }
        if (code == Code.Ldftn && instruction.Operand is MethodReference functionMethod)
        {
            var target = functionMethod.Resolve() ?? throw Unsupported(context.Method, instruction, "function pointer target could not be resolved");
            if (target.Module != context.Method.Module || target.IsStatic)
                throw Unsupported(context.Method, instruction, "ldftn currently requires an instance method in the generated mod assembly");
            stack.Push(new QuestValue("", "System.IntPtr", false, FunctionPointerTarget: target));
            return true;
        }
        if (TryLoadAddress(context, instruction, stack))
            return true;
        if (IlArrayLowerer.TryLower(context, instruction, stack, out statement))
            return true;
        if (code is Code.Ldfld or Code.Stfld or Code.Ldflda)
        {
            LowerInstanceField(context, instruction, stack, out statement);
            return true;
        }
        if (code == Code.Initobj && instruction.Operand is TypeReference initializedType)
        {
            var address = Pop(context, instruction, stack);
            if (!address.IsByRef || address.PcType != initializedType.FullName || !CanTakeAddress(initializedType.FullName))
                throw new InvalidOperationException($"Unsupported IL in {context.Method.FullName} at IL_{instruction.Offset:x4} ({instruction.OpCode.Code}): initobj requires a by-reference address of a supported primitive value type.");
            statement = Raw($"(*({address.Code})) = {{}};", "IL initialize object");
            return true;
        }
        if (code == Code.Cpobj && instruction.Operand is TypeReference copiedType)
        {
            var source = Pop(context, instruction, stack);
            var destination = Pop(context, instruction, stack);
            if (!source.IsByRef || !destination.IsByRef || source.PcType != copiedType.FullName || destination.PcType != copiedType.FullName || !CanTakeAddress(copiedType.FullName))
                throw Unsupported(context.Method, instruction, "cpobj requires source and destination addresses of the same supported primitive value type");
            statement = Raw($"(*({destination.Code})) = (*({source.Code}));", "IL copy object");
            return true;
        }
        if (code == Code.Ldsfld && instruction.Operand is FieldReference loadField)
        {
            if (loadField.Resolve() is { } compilerField && IlTranslationContext.IsCompilerDelegateCache(compilerField))
            {
                if (compilerField.Name.StartsWith("<>9__", StringComparison.Ordinal))
                    stack.Push(new QuestValue("nullptr", compilerField.FieldType.FullName, false));
                else
                {
                    var nativeClosure = QuestApiTypeMapper.Map(compilerField.FieldType) ?? throw Unsupported(context.Method, instruction, "compiler closure type has no Quest representation");
                    stack.Push(new QuestValue($"{nativeClosure.TrimEnd('*')}::New_ctor()", compilerField.FieldType.FullName, false));
                }
                return true;
            }
            if (!context.StaticFields.TryGetValue(loadField.Resolve()?.FullName ?? loadField.FullName, out var staticValue))
                throw Unsupported(context.Method, instruction);
            var fieldName = loadField.Resolve()?.FullName ?? loadField.FullName;
            if (context.StaticFieldInitializers.TryGetValue(fieldName, out var initializer))
            {
                var lazyValue = $"([&]() {{ if ({staticValue.Code} == nullptr) {staticValue.Code} = {initializer}; return {staticValue.Code}; }}())";
                stack.Push(staticValue with { Code = lazyValue });
            }
            else
                stack.Push(staticValue);
            return true;
        }
        if (code == Code.Ldsflda && instruction.Operand is FieldReference addressField)
        {
            var field = addressField.Resolve();
            var fieldName = field?.FullName ?? addressField.FullName;
            if (field is null || !field.IsStatic || field.Module != context.Method.Module || field.IsPublic || field.IsInitOnly || !CanTakeAddress(field.FieldType.FullName) || !context.StaticFields.TryGetValue(fieldName, out var staticValue) || context.ConfigFields.ContainsKey(fieldName))
                throw Unsupported(context.Method, instruction, "ldsflda supports only internal mutable primitive static fields with emitted local storage");
            stack.Push(staticValue with { Code = $"&({staticValue.Code})", IsByRef = true });
            return true;
        }
        if (code == Code.Stsfld && instruction.Operand is FieldReference storeField)
        {
            if (storeField.Resolve() is { } compilerField && IlTranslationContext.IsCompilerDelegateCache(compilerField))
            {
                var cachedDelegate = Pop(context, instruction, stack);
                statement = Raw($"(void)({cachedDelegate.Code});", "IL compiler delegate cache store");
                return true;
            }
            var fieldName = storeField.Resolve()?.FullName ?? storeField.FullName;
            if (!context.StaticFields.TryGetValue(fieldName, out var staticValue))
                throw Unsupported(context.Method, instruction);
            var value = Pop(context, instruction, stack);
            if (value.DeferredBoxedPrimitiveType is not null)
                throw Unsupported(context.Method, instruction, "a deferred boxed primitive cannot be stored because no Quest object was allocated");
            if (context.ConfigFields.TryGetValue(fieldName, out var configField))
            {
                statement = Raw($"getModConfig().{configField.Name}.SetValue({value.Code});", "IL Quest config assignment");
                return true;
            }
            statement = Raw($"{staticValue.Code} = {value.Code};", "IL static field assignment");
            return true;
        }
        if (TryIndirect(context, instruction, stack, out statement))
            return true;
        if (code == Code.Sizeof && instruction.Operand is TypeReference sizedType)
        {
            if (!IlArrayLowerer.TryCppType(sizedType.FullName, out var cppType))
                throw Unsupported(context.Method, instruction, "sizeof is supported only for primitive value types with a verified Quest representation");
            stack.Push(new QuestValue($"static_cast<uint32_t>(sizeof({cppType}))", "System.UInt32", false));
            return true;
        }
        if (TryNumericConstant(instruction, out var number))
        {
            stack.Push(number);
            return true;
        }
        if (code == Code.Ldstr && instruction.Operand is string text)
        {
            stack.Push(new QuestValue(text, "System.String", true));
            return true;
        }
        if (code == Code.Ldnull)
        {
            stack.Push(new QuestValue("nullptr", "System.Object", false));
            return true;
        }
        if (code == Code.Box)
        {
            if (instruction.Operand is not TypeReference boxedType || !IlArrayLowerer.TryCppType(boxedType.FullName, out _) || stack.Count == 0)
                throw Unsupported(context.Method, instruction, "only supported primitive values can be deferred for the special logger path");
            var value = Pop(context, instruction, stack);
            if (value.IsByRef || value.DeferredBoxedPrimitiveType is not null || value.PcType != boxedType.FullName)
                throw Unsupported(context.Method, instruction, "box operand must exactly match its supported primitive type");
            stack.Push(value with { PcType = "System.Object", DeferredBoxedPrimitiveType = boxedType.FullName });
            return true;
        }
        if (code == Code.Pop)
        {
            var popped = Pop(context, instruction, stack);
            if (!popped.IsStringLiteral)
                statement = Raw($"(void)({popped.Code});", "IL pop", popped.ApiMapping);
            return true;
        }
        if (code == Code.Dup)
        {
            var top = Pop(context, instruction, stack);
            if (top.PcType.EndsWith("[]", StringComparison.Ordinal))
            {
                var temporary = $"il_dup_temp_{context.TemporaryIndex++}";
                statement = Raw($"auto&& {temporary} = ({top.Code});", "IL duplicate array materialization", top.ApiMapping);
                top = top with { Code = temporary, IsStringLiteral = false };
            }
            else if (top.ApiMapping is { } apiMapping && !top.IsStringLiteral)
            {
                var temporary = $"il_dup_temp_{context.TemporaryIndex++}";
                var cppType = apiMapping.QuestReturnConversion switch
                {
                    "StringViewToStringW" => "::StringW",
                    "PointerIsNonNull" => "bool",
                    _ => apiMapping.QuestReturn,
                };
                statement = Raw($"{cppType} {temporary} = {top.Code};", "IL duplicate materialization", apiMapping);
                top = top with { Code = temporary, IsStringLiteral = false };
            }
            stack.Push(top);
            stack.Push(top);
            return true;
        }
        if (TryLocalStore(context, instruction, stack, out statement))
            return true;
        if (TryArgumentStore(context, instruction, stack, out statement))
            return true;
        if (TryBinary(context, instruction, stack) || TryUnary(instruction, stack) || TryConvert(instruction, stack))
            return true;
        if (code == Code.Newobj && instruction.Operand is MethodReference constructor)
        {
            LowerNewObject(context, instruction, constructor, stack);
            return true;
        }
        if (code is Code.Call or Code.Callvirt && instruction.Operand is MethodReference call)
        {
            statement = LowerCall(context, instruction, call, stack);
            return true;
        }
        throw Unsupported(context.Method, instruction);
    }

    public static bool TryEvaluateCondition(IlTranslationContext context, int start, int end, Instruction branch, out string condition)
    {
        condition = "";
        var stack = new Stack<QuestValue>();
        var locals = context.Locals.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var instruction in context.Instructions.Skip(start).Take(end - start))
        {
            var index = context.InstructionIndexes[instruction];
            if (instruction.OpCode.Code == Code.Nop)
                continue;
            if (TryLoadLocal(context, instruction, stack, locals) || TryLoadArgument(context, instruction, stack))
                continue;
            if (TryNumericConstant(instruction, out var number))
            {
                stack.Push(number);
                continue;
            }
            if (TryBinary(context, instruction, stack))
                continue;
            return false;
        }
        if (IsRelationalBranch(branch.OpCode.Code))
        {
            if (stack.Count != 2)
                return false;
            var right = stack.Pop();
            var left = stack.Pop();
            condition = IlComparisonLowerer.Format(left, right, branch.OpCode.Code);
            return true;
        }
        if (branch.OpCode.Code is Code.Brtrue or Code.Brtrue_S or Code.Brfalse or Code.Brfalse_S)
        {
            if (stack.Count != 1)
                return false;
            var value = stack.Pop();
            var truth =
                IsBool(value.PcType) ? value.Code
                : value.PcType == "System.Object" ? $"({value.Code} != nullptr)"
                : $"({value.Code} != 0)";
            condition = branch.OpCode.Code is Code.Brtrue or Code.Brtrue_S ? truth : $"!({truth})";
            return true;
        }
        return false;
    }

    private static void LowerNewObject(IlTranslationContext context, Instruction instruction, MethodReference constructor, Stack<QuestValue> stack)
    {
        var definition = constructor.Resolve() ?? throw Unsupported(context.Method, instruction, "constructor could not be resolved");
        if (definition.DeclaringType.BaseType?.FullName == "System.MulticastDelegate")
        {
            if (constructor.Parameters.Count != 2)
                throw Unsupported(context.Method, instruction, "delegate constructors must have the standard (target, function pointer) shape");
            var functionPointer = Pop(context, instruction, stack);
            var target = Pop(context, instruction, stack);
            if (functionPointer.FunctionPointerTarget is null)
                throw Unsupported(context.Method, instruction, "delegate target must be an IL function pointer from ldftn");
            var delegateType = QuestApiTypeMapper.Map(constructor.DeclaringType) ?? throw Unsupported(context.Method, instruction, "delegate type has no Quest representation");
            var method = functionPointer.FunctionPointerTarget;
            var thunk = GeneratedModTypeEmitter.FunctionThunkName(method);
            var methodPointer = $"::System::IntPtr(reinterpret_cast<void*>(&{thunk}))";
            var nativeDelegateType = delegateType.TrimEnd('*');
            var delegateConstruction = $"{nativeDelegateType}::New_ctor({target.Code}, {methodPointer})";
            stack.Push(new QuestValue(delegateConstruction, constructor.DeclaringType.FullName, false));
            return;
        }
        if (!definition.IsConstructor || definition.IsStatic)
            throw Unsupported(context.Method, instruction, "newobj requires an instance constructor");
        if (definition.DeclaringType.IsValueType)
        {
            var nativeValueType = QuestApiTypeMapper.Map(constructor.DeclaringType) ?? throw Unsupported(context.Method, instruction, "constructed value type has no Quest representation");
            var valueArguments = new QuestValue[constructor.Parameters.Count];
            for (var i = valueArguments.Length - 1; i >= 0; i--)
                valueArguments[i] = Pop(context, instruction, stack);
            stack.Push(new QuestValue($"{nativeValueType}({string.Join(", ", valueArguments.Select(AsCppValue))})", constructor.DeclaringType.FullName, false));
            return;
        }
        var nativeType = QuestApiTypeMapper.Map(constructor.DeclaringType) ?? throw Unsupported(context.Method, instruction, "constructed type has no Quest representation");
        var questType = nativeType.TrimEnd('*');
        var arguments = new QuestValue[constructor.Parameters.Count];
        for (var i = arguments.Length - 1; i >= 0; i--)
        {
            arguments[i] = Pop(context, instruction, stack);
            if (arguments[i].IsByRef)
                throw Unsupported(context.Method, instruction, "constructor by-reference arguments require an explicit ABI mapping");
        }
        var argumentTypes = constructor.Parameters.Select(parameter => QuestApiTypeMapper.Map(parameter.ParameterType) ?? throw Unsupported(context.Method, instruction, $"constructor parameter {parameter.ParameterType.FullName} has no Quest representation")).ToArray();
        var publicHeader = questType.Split('<')[0].TrimStart(':').Replace("::", "/", StringComparison.Ordinal) + ".hpp";
        var declarationHeader = Path.Combine(Path.GetDirectoryName(publicHeader) ?? "", "zzzz__" + questType.Split('<')[0].Split("::").Last() + "_def.hpp").Replace('\\', '/');
        var mapping = new ApiCallMapping(
            context.Catalog.Profile.Id,
            definition.Module.Assembly.Name.Name,
            definition.DeclaringType.FullName,
            definition.Name,
            definition.GenericParameters.Count,
            definition.ReturnType.FullName,
            definition.Parameters.Select(parameter => parameter.ParameterType.FullName).ToArray(),
            false,
            questType.TrimStart(':'),
            publicHeader,
            declarationHeader,
            "New_ctor",
            nativeType,
            argumentTypes,
            true
        );
        var code = $"::{questType.TrimStart(':')}::New_ctor({string.Join(", ", arguments.Select(AsCppValue))})";
        stack.Push(new QuestValue(code, constructor.DeclaringType.FullName, false, mapping));
    }

    public static bool TryEvaluateValue(IlTranslationContext context, int start, int end, out QuestValue value)
    {
        value = null!;
        var stack = new Stack<QuestValue>();
        var locals = context.Locals.ToDictionary(pair => pair.Key, pair => pair.Value);
        for (var index = start; index < end; index++)
        {
            var instruction = context.Instructions[index];
            if (instruction.OpCode.Code == Code.Nop)
                continue;
            if (TryLoadLocal(context, instruction, stack, locals) || TryLoadArgument(context, instruction, stack))
                continue;
            if (TryNumericConstant(instruction, out var number))
            {
                stack.Push(number);
                continue;
            }
            if (!TryLower(context, instruction, stack, out var statement) || statement is not null)
                return false;
        }
        if (stack.Count != 1)
            return false;
        value = stack.Pop();
        return !value.IsByRef && value.DeferredBoxedPrimitiveType is null;
    }

    public static bool TryLowerCondition(IlTranslationContext context, int start, int end, Instruction branch, out string condition, out IReadOnlyList<QuestLogStatement> statements)
    {
        condition = "";
        var lowered = new List<QuestLogStatement>();
        var stack = new Stack<QuestValue>();
        for (var index = start; index < end; index++)
        {
            var instruction = context.Instructions[index];
            if (!TryLower(context, instruction, stack, out var statement))
            {
                statements = Array.Empty<QuestLogStatement>();
                return false;
            }
            if (statement is not null)
                lowered.Add(statement);
        }

        if (IsRelationalBranch(branch.OpCode.Code))
        {
            if (stack.Count != 2)
            {
                statements = Array.Empty<QuestLogStatement>();
                return false;
            }
            var right = stack.Pop();
            var left = stack.Pop();
            condition = IlComparisonLowerer.Format(left, right, branch.OpCode.Code);
        }
        else if (branch.OpCode.Code is Code.Brtrue or Code.Brtrue_S or Code.Brfalse or Code.Brfalse_S)
        {
            if (stack.Count != 1)
            {
                statements = Array.Empty<QuestLogStatement>();
                return false;
            }
            var value = stack.Pop();
            var truth =
                IsBool(value.PcType) ? value.Code
                : value.PcType == "System.Object" ? $"({value.Code} != nullptr)"
                : $"({value.Code} != 0)";
            condition = branch.OpCode.Code is Code.Brtrue or Code.Brtrue_S ? truth : $"!({truth})";
        }
        else
        {
            statements = Array.Empty<QuestLogStatement>();
            return false;
        }
        statements = lowered;
        return true;
    }

    public static bool IsConditionalBranch(Code code) => IsRelationalBranch(code) || code is Code.Brtrue or Code.Brtrue_S or Code.Brfalse or Code.Brfalse_S;

    public static bool IsRelationalBranch(Code code) =>
        code
            is Code.Beq
                or Code.Beq_S
                or Code.Bne_Un
                or Code.Bne_Un_S
                or Code.Bgt
                or Code.Bgt_S
                or Code.Bgt_Un
                or Code.Bgt_Un_S
                or Code.Bge
                or Code.Bge_S
                or Code.Bge_Un
                or Code.Bge_Un_S
                or Code.Blt
                or Code.Blt_S
                or Code.Blt_Un
                or Code.Blt_Un_S
                or Code.Ble
                or Code.Ble_S
                or Code.Ble_Un
                or Code.Ble_Un_S;

    private static bool TryLoadArgument(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack)
    {
        var code = instruction.OpCode.Code;
        var index =
            code is >= Code.Ldarg_0 and <= Code.Ldarg_3 ? (int)code - (int)Code.Ldarg_0
            : code is Code.Ldarg or Code.Ldarg_S && instruction.Operand is ParameterDefinition parameter ? parameter.Index + (context.Method.IsStatic ? 0 : 1)
            : -1;
        if (index < 0)
            return false;
        if (!context.Arguments.TryGetValue(index, out var argument))
            throw new InvalidOperationException($"Harmony parameter index {index} in {context.Method.FullName} is not bound to a Quest hook argument.");
        stack.Push(argument);
        return true;
    }

    private static bool TryIndirect(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack, out QuestLogStatement? statement)
    {
        statement = null;
        var code = instruction.OpCode.Code;
        if (code is Code.Ldind_I1 or Code.Ldind_U1 or Code.Ldind_I2 or Code.Ldind_U2 or Code.Ldind_I4 or Code.Ldind_U4 or Code.Ldind_I8 or Code.Ldind_I or Code.Ldind_R4 or Code.Ldind_R8 or Code.Ldind_Ref or Code.Ldobj)
        {
            var address = Pop(context, instruction, stack);
            if (!address.IsByRef)
                throw Unsupported(context.Method, instruction);
            var expectedType =
                instruction.OpCode.Code == Code.Ldobj ? (instruction.Operand as TypeReference)?.FullName
                : instruction.OpCode.Code == Code.Ldind_Ref ? address.PcType
                : IndirectType(instruction.OpCode.Code);
            if (!IsSupportedIndirectType(expectedType, instruction.OpCode.Code, context.Method.Module) || !IndirectAddressMatches(instruction.OpCode.Code, address.PcType, expectedType))
                throw Unsupported(context.Method, instruction);
            stack.Push(new QuestValue($"(*({address.Code}))", expectedType!, false, address.ApiMapping));
            return true;
        }
        if (code is Code.Stind_I1 or Code.Stind_I2 or Code.Stind_I4 or Code.Stind_I8 or Code.Stind_I or Code.Stind_R4 or Code.Stind_R8 or Code.Stind_Ref or Code.Stobj)
        {
            var value = Pop(context, instruction, stack);
            var address = Pop(context, instruction, stack);
            var expectedType =
                instruction.OpCode.Code == Code.Stobj ? (instruction.Operand as TypeReference)?.FullName
                : instruction.OpCode.Code == Code.Stind_Ref ? address.PcType
                : IndirectType(instruction.OpCode.Code);
            if (!address.IsByRef || !IsSupportedIndirectType(expectedType, instruction.OpCode.Code, context.Method.Module) || !IndirectAddressMatches(instruction.OpCode.Code, address.PcType, expectedType) || !IndirectStoreValueMatches(instruction.OpCode.Code, value.PcType, expectedType))
                throw Unsupported(context.Method, instruction);
            statement = Raw($"(*({address.Code})) = {value.Code};", "IL indirect assignment", value.ApiMapping);
            return true;
        }
        return false;
    }

    private static string? IndirectType(Code code) =>
        code switch
        {
            Code.Ldind_I1 or Code.Stind_I1 => "System.SByte",
            Code.Ldind_U1 => "System.Byte",
            Code.Ldind_I2 or Code.Stind_I2 => "System.Int16",
            Code.Ldind_U2 => "System.UInt16",
            Code.Ldind_I4 or Code.Stind_I4 => "System.Int32",
            Code.Ldind_U4 => "System.UInt32",
            Code.Ldind_I8 or Code.Stind_I8 => "System.Int64",
            Code.Ldind_I or Code.Stind_I => "System.IntPtr",
            Code.Ldind_R4 or Code.Stind_R4 => "System.Single",
            Code.Ldind_R8 or Code.Stind_R8 => "System.Double",
            _ => null,
        };

    private static bool IsSupportedIndirectType(string? type, Code code, ModuleDefinition module) =>
        type is "System.Boolean" or "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" or "System.Single" or "System.Double" or "System.IntPtr" or "System.UIntPtr"
        || code is Code.Ldind_Ref or Code.Stind_Ref && type is not null
        || code is Code.Ldobj or Code.Stobj && type is not null && IsMappedValueType(module, type);

    private static bool IsMappedValueType(ModuleDefinition module, string fullName)
    {
        var definition = module.GetType(fullName);
        if (definition is not null)
            return definition.IsValueType && QuestApiTypeMapper.Map(definition) is not null;
        foreach (var reference in module.AssemblyReferences)
            try
            {
                var resolved = module.AssemblyResolver.Resolve(reference).MainModule.GetType(fullName);
                if (resolved is not null)
                    return resolved.IsValueType && QuestApiTypeMapper.Map(resolved) is not null;
            }
            catch (AssemblyResolutionException) { }
        return false;
    }

    private static bool IndirectAddressMatches(Code code, string addressType, string? opcodeType)
    {
        if (code is Code.Ldobj or Code.Stobj)
            return addressType == opcodeType;
        if (code is Code.Ldind_Ref or Code.Stind_Ref)
            return addressType == opcodeType;
        return StorageWidth(addressType) == StorageWidth(opcodeType);
    }

    private static bool IndirectStoreValueMatches(Code code, string valueType, string? storageType)
    {
        if (code == Code.Stind_Ref)
            return valueType == storageType || valueType == "System.Object";
        if (code == Code.Stobj)
            return valueType == storageType;
        if (code == Code.Stind_R4)
            return valueType == "System.Single";
        if (code == Code.Stind_R8)
            return valueType == "System.Double";
        var width = StorageWidth(storageType);
        return width == 1 || width == 2 || width == 4 ? valueType is "System.Int32" or "System.UInt32" or "System.Boolean"
            : width == 8 ? valueType is "System.Int64" or "System.UInt64"
            : width == 9 ? valueType is "System.IntPtr" or "System.UIntPtr"
            : false;
    }

    private static int StorageWidth(string? type) =>
        type switch
        {
            "System.Boolean" or "System.Byte" or "System.SByte" => 1,
            "System.Int16" or "System.UInt16" => 2,
            "System.Int32" or "System.UInt32" => 4,
            "System.Int64" or "System.UInt64" or "System.Double" => 8,
            "System.Single" => 4,
            "System.IntPtr" or "System.UIntPtr" => 9,
            _ => 0,
        };

    private static bool TryLoadAddress(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack)
    {
        var code = instruction.OpCode.Code;
        if (code is Code.Ldarga or Code.Ldarga_S && instruction.Operand is ParameterDefinition parameter)
        {
            var argumentIndex = parameter.Index + (context.Method.IsStatic ? 0 : 1);
            if (!context.Arguments.TryGetValue(argumentIndex, out var argument) || argument.IsByRef || !CanTakeAddress(parameter.ParameterType))
                throw Unsupported(context.Method, instruction);
            stack.Push(argument with { Code = $"&({argument.Code})", IsByRef = true });
            return true;
        }
        if (code is Code.Ldloca or Code.Ldloca_S && instruction.Operand is VariableDefinition variable)
        {
            if (!context.Locals.TryGetValue(variable.Index, out var local) || !CanTakeAddress(context.Method.Body.Variables[variable.Index].VariableType))
                throw Unsupported(context.Method, instruction);
            stack.Push(local with { Code = $"&({local.Code})", IsByRef = true });
            return true;
        }
        return false;
    }

    private static void LowerInstanceField(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack, out QuestLogStatement? statement)
    {
        statement = null;
        if (instruction.Operand is not FieldReference reference || reference.Resolve() is not { } field || field.IsStatic || field.DeclaringType.IsValueType)
            throw Unsupported(context.Method, instruction);
        var primitiveField = CanTakeAddress(field.FieldType.FullName);
        var enumField = field.FieldType.Resolve()?.IsEnum == true;
        var referenceField = field.FieldType.MetadataType is MetadataType.Class or MetadataType.String or MetadataType.Object || field.FieldType.IsArray || field.FieldType is GenericInstanceType;
        var mappedValueField = field.FieldType.IsValueType && QuestApiTypeMapper.Map(field.FieldType) is not null;
        if (!primitiveField && !enumField && !referenceField && !mappedValueField)
            throw Unsupported(context.Method, instruction, "field type has no verified Quest representation");
        if (instruction.OpCode.Code == Code.Ldflda && !primitiveField && !enumField)
            throw Unsupported(context.Method, instruction, "ldflda is supported only for primitive value fields");
        var assigned = instruction.OpCode.Code == Code.Stfld ? Pop(context, instruction, stack) : null;
        var receiver = Pop(context, instruction, stack);
        if (receiver.IsByRef || receiver.PcType != field.DeclaringType.FullName || receiver.Code == "nullptr")
            throw Unsupported(context.Method, instruction);
        var access = $"{receiver.Code}->___{field.Name}";
        var value = new QuestValue(access, field.FieldType.FullName, false);
        if (instruction.OpCode.Code == Code.Ldfld)
        {
            stack.Push(value);
            return;
        }
        if (instruction.OpCode.Code == Code.Ldflda)
        {
            stack.Push(value with { Code = $"&({access})", IsByRef = true });
            return;
        }
        var boolFromInt32 = field.FieldType.MetadataType == MetadataType.Boolean && assigned?.PcType == "System.Int32";
        var enumFromInt32 = enumField && assigned?.PcType == "System.Int32";
        if (assigned is null || assigned.PcType != field.FieldType.FullName && !boolFromInt32 && !enumFromInt32 || assigned.IsByRef)
            throw Unsupported(context.Method, instruction);
        var assignedCode = enumField ? $"static_cast<int32_t>({AsCppValue(assigned)})" : AsCppValue(assigned);
        statement = Raw($"{access} = {assignedCode};", "IL instance field assignment", assigned.ApiMapping);
    }

    private static bool CanTakeAddress(string type) => type is "System.Boolean" or "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" or "System.Single" or "System.Double";

    private static bool CanTakeAddress(TypeReference type) => CanTakeAddress(type.FullName) || type.IsValueType && QuestApiTypeMapper.Map(type) is not null;

    private static bool TryLoadLocal(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack, IReadOnlyDictionary<int, QuestValue>? locals = null)
    {
        if (!TryGetLocalIndex(instruction, load: true, out var index))
            return false;
        if (!(locals ?? context.Locals).TryGetValue(index, out var local))
            throw Unsupported(context.Method, instruction);
        stack.Push(local);
        return true;
    }

    private static bool TryLocalStore(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack, out QuestLogStatement? statement)
    {
        statement = null;
        if (!TryGetLocalIndex(instruction, load: false, out var index))
            return false;
        var value = Pop(context, instruction, stack);
        var local = context.Locals[index];
        if (value.DeferredBoxedPrimitiveType is not null)
            throw Unsupported(context.Method, instruction, "a deferred boxed primitive can only flow directly to the special logger path");
        statement = Raw($"{local.Code} = {value.Code};", "IL local assignment", value.ApiMapping);
        return true;
    }

    private static bool TryArgumentStore(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack, out QuestLogStatement? statement)
    {
        statement = null;
        if (instruction.OpCode.Code is not (Code.Starg or Code.Starg_S) || instruction.Operand is not ParameterDefinition parameter)
            return false;
        if (!context.Arguments.TryGetValue(parameter.Index, out var argument) || argument.IsByRef)
            throw Unsupported(context.Method, instruction, "starg requires a bound argument with ordinary value storage");
        var value = Pop(context, instruction, stack);
        if (value.DeferredBoxedPrimitiveType is not null)
            throw Unsupported(context.Method, instruction, "a deferred boxed primitive can only flow directly to the special logger path");
        if (value.IsByRef || value.PcType != argument.PcType)
            throw Unsupported(context.Method, instruction, $"assigned value must match argument type '{argument.PcType}'");
        statement = Raw($"{argument.Code} = {value.Code};", "IL argument assignment", value.ApiMapping);
        return true;
    }

    private static QuestLogStatement? LowerCall(IlTranslationContext context, Instruction instruction, MethodReference call, Stack<QuestValue> stack)
    {
        if (IlReflectionLowerer.TryLowerCall(context, instruction, call, stack, out var reflectionStatement))
            return reflectionStatement;

        if (TryGetLoggingLevel(call, out var level))
        {
            var value = Pop(context, instruction, stack);
            if (call.Parameters[0].ParameterType.FullName == "System.String" && value.PcType != "System.String")
                throw Unsupported(context.Method, instruction);
            if (value.DeferredBoxedPrimitiveType is not null && call.Parameters[0].ParameterType.FullName != "System.Object")
                throw Unsupported(context.Method, instruction, "deferred boxed primitives can only be consumed by the special object logger path");
            return new QuestLogStatement(value.IsStringLiteral ? value.Code : "", level, call.FullName, value.IsStringLiteral ? null : value.Code, value.ApiMapping);
        }

        if (context.Method.IsConstructor && call.Name == ".ctor" && call.DeclaringType.FullName != context.Method.DeclaringType.FullName && call.HasThis)
        {
            _ = Pop(context, instruction, stack);
            return Raw("", "IL base constructor call");
        }

        if (IsSameModAssembly(context.Method.Module, call.DeclaringType.Scope.Name))
            return LowerModLocalCall(context, instruction, call, stack);

        var mapping = context.Catalog.ResolveApiCall(call) ?? throw new InvalidOperationException($"No exact PC-to-Quest API mapping for {call.DeclaringType.Scope.Name}:{call.FullName} in profile '{context.Catalog.Profile.Id}'.");
        if (mapping.QuestStatic == call.HasThis || mapping.QuestParameters.Length != call.Parameters.Count)
            throw Unsupported(context.Method, instruction);
        var args = new QuestValue[call.Parameters.Count];
        for (var i = args.Length - 1; i >= 0; i--)
        {
            var argument = Pop(context, instruction, stack);
            if (argument.DeferredBoxedPrimitiveType is not null)
                throw Unsupported(context.Method, instruction, "a deferred boxed primitive cannot be passed to an arbitrary Quest API because no Quest object was allocated");
            var pcParameterType = call.Parameters[i].ParameterType.FullName;
            var conversion = mapping.QuestArgumentConversions?.ElementAtOrDefault(i);
            if (conversion is not null)
            {
                argument = conversion switch
                {
                    "Identity" => argument,
                    "StringToUtf8StringView" when pcParameterType == "System.String" => argument with { Code = $"std::string({AsCppValue(argument)})", IsStringLiteral = false },
                    _ => throw Unsupported(context.Method, instruction, $"unknown Quest library argument conversion '{conversion}' for parameter {i}"),
                };
            }
            var closedParameter = ResolveClosedCallType(context, call.Parameters[i].ParameterType, call);
            var closedNativeParameter = QuestApiTypeMapper.Map(closedParameter) ?? mapping.QuestParameters[i];
            args[i] = argument with { Code = IlInterfaceConversion.ConvertArgument(context, argument, closedParameter, closedNativeParameter) };
        }
        var receiver = call.HasThis ? Pop(context, instruction, stack) : null;
        if (receiver?.DeferredBoxedPrimitiveType is not null)
            throw Unsupported(context.Method, instruction, "a deferred boxed primitive cannot be used as an API receiver because no Quest object was allocated");
        var argumentCode = string.Join(", ", args.Select(argument => argument.Code));
        var genericArguments =
            call is GenericInstanceMethod genericCall && genericCall.GenericArguments.Count > 0
                ? "<" + string.Join(", ", genericCall.GenericArguments.Select(argument => QuestApiTypeMapper.Map(context.ResolveType(argument)) ?? throw Unsupported(context.Method, instruction, $"generic argument '{argument.FullName}' has no Quest type mapping"))) + ">"
                : "";
        var callName = mapping.QuestMethod + genericArguments;
        var receiverCode = receiver is { IsByRef: true } ? $"(*({receiver.Code}))" : receiver?.Code;
        if (receiver is not null && context.FindKnownType(receiver.PcType) is { } receiverType && IlInterfaceConversion.TryConvert(receiverType, call.DeclaringType, receiverCode!, out var convertedReceiver, call.Name))
            receiverCode = convertedReceiver;
        var callCode = receiver is null ? $"::{mapping.QuestType}::{callName}({argumentCode})" : $"{receiverCode}{(call.DeclaringType.IsValueType ? "." : "->")}{callName}({argumentCode})";
        if (call.ReturnType.MetadataType == MetadataType.Void)
            return Raw(callCode + ";", call.FullName, mapping);
        callCode = mapping.QuestReturnConversion switch
        {
            null or "Identity" => callCode,
            "PointerIsNonNull" => $"({callCode} != nullptr)",
            "StringViewToStringW" => $"StringW(std::string({callCode}))",
            var conversion => throw Unsupported(context.Method, instruction, $"unknown Quest library return conversion '{conversion}'"),
        };
        var returnType = ResolveClosedCallType(context, call.ReturnType, call);
        stack.Push(new QuestValue(callCode, returnType.FullName, false, mapping));
        return null;
    }

    private static string? MapFullNameToNativeReference(string fullName)
    {
        if (fullName.StartsWith("System.", StringComparison.Ordinal))
            return null;
        var parts = fullName.Split('.');
        var nativeName = parts.Length == 1 ? "::GlobalNamespace::" + parts[0] : "::" + string.Join("::", parts);
        return nativeName + "*";
    }

    private static TypeReference ResolveClosedCallType(IlTranslationContext context, TypeReference type, MethodReference call)
    {
        if (type is GenericParameter parameter)
        {
            if (parameter.Type == GenericParameterType.Method && call is GenericInstanceMethod method && parameter.Position < method.GenericArguments.Count)
                return context.ResolveType(method.GenericArguments[parameter.Position]);
            if (parameter.Type == GenericParameterType.Type && call.DeclaringType is GenericInstanceType declaring && parameter.Position < declaring.GenericArguments.Count)
                return context.ResolveType(declaring.GenericArguments[parameter.Position]);
        }
        if (type is ArrayType array)
        {
            var element = ResolveClosedCallType(context, array.ElementType, call);
            return array.IsVector ? new ArrayType(element) : new ArrayType(element, array.Rank);
        }
        if (type is GenericInstanceType generic)
        {
            var instance = new GenericInstanceType(generic.ElementType);
            foreach (var argument in generic.GenericArguments)
                instance.GenericArguments.Add(ResolveClosedCallType(context, argument, call));
            return instance;
        }
        return context.ResolveType(type);
    }

    private static QuestLogStatement? LowerModLocalCall(IlTranslationContext context, Instruction instruction, MethodReference call, Stack<QuestValue> stack)
    {
        var method = call.Resolve() ?? throw new InvalidOperationException($"Could not resolve mod-local method {call.FullName}.");
        var hasReceiver = call.HasThis;
        if (method.Module != context.Method.Module || !method.HasBody || method.IsStatic == hasReceiver)
            throw new InvalidOperationException($"Mod-local call {call.FullName} is outside the supported subset (same-module methods with a body).");
        var genericArguments = new Dictionary<int, TypeReference>();
        if (method.GenericParameters.Count != 0)
        {
            if (call is not GenericInstanceMethod closedCall || closedCall.GenericArguments.Count != method.GenericParameters.Count)
                throw new InvalidOperationException($"Mod-local generic call {call.FullName} must provide closed generic arguments.");
            for (var i = 0; i < closedCall.GenericArguments.Count; i++)
            {
                var resolvedArgument = context.ResolveType(closedCall.GenericArguments[i]);
                if (!CanInlineHelperType(resolvedArgument))
                    throw new InvalidOperationException($"Generic argument '{resolvedArgument.FullName}' on mod-local call {call.FullName} is outside the inline scalar subset.");
                genericArguments[i] = resolvedArgument;
            }
        }
        var effectiveParameters = method.Parameters.Select(parameter => ResolveType(parameter.ParameterType, genericArguments)).ToArray();
        var effectiveReturn = ResolveType(method.ReturnType, genericArguments);
        if (effectiveParameters.Any(parameter => !CanInlineHelperType(parameter)) || !CanInlineHelperType(effectiveReturn))
            throw new InvalidOperationException($"Mod-local helper {method.FullName} has a parameter or return type outside the inline scalar subset.");
        if (!context.ActiveLocalMethods.Add(method.FullName))
            throw new InvalidOperationException($"Recursive mod-local helper call cycle is not supported: {call.FullName}.");

        try
        {
            var arguments = new QuestValue[call.Parameters.Count];
            for (var index = arguments.Length - 1; index >= 0; index--)
            {
                arguments[index] = Pop(context, instruction, stack);
                var helperParameter = effectiveParameters[index];
                var helperIsByReference = helperParameter is ByReferenceType;
                var helperValueType = helperIsByReference ? ((ByReferenceType)helperParameter).ElementType : helperParameter;
                var enumFromInt32 = helperValueType.Resolve()?.IsEnum == true && arguments[index].PcType == "System.Int32";
                var booleanFromInt32 = helperValueType.MetadataType == MetadataType.Boolean && arguments[index].PcType == "System.Int32";
                if (arguments[index].IsByRef != helperIsByReference || arguments[index].PcType != helperValueType.FullName && !enumFromInt32 && !booleanFromInt32)
                    throw Unsupported(context.Method, instruction, $"helper argument {index} does not match {method.Parameters[index].ParameterType.FullName}");
            }

            var receiver = hasReceiver ? Pop(context, instruction, stack) : null;
            if (receiver is { IsByRef: true } || receiver is not null && receiver.PcType != method.DeclaringType.FullName)
                throw Unsupported(context.Method, instruction, $"helper receiver does not match {method.DeclaringType.FullName}");

            var lambdaParameters = new List<string>();
            var lambdaArguments = new List<string>();
            var helperArguments = new Dictionary<int, QuestValue>();
            if (receiver is not null)
            {
                lambdaParameters.Add((QuestApiTypeMapper.Map(method.DeclaringType) ?? throw Unsupported(context.Method, instruction, "helper receiver has no Quest type")) + " helper_this");
                lambdaArguments.Add(receiver.Code);
                helperArguments[0] = receiver with { Code = "helper_this" };
            }
            for (var index = 0; index < method.Parameters.Count; index++)
            {
                var name = "helper_arg_" + index;
                var helperParameter = effectiveParameters[index];
                var helperIsByReference = helperParameter is ByReferenceType;
                var helperValueType = helperIsByReference ? ((ByReferenceType)helperParameter).ElementType : helperParameter;
                var cppType = helperIsByReference ? IlControlFlowLowerer.CppLocalType(helperValueType, method) + "&" : "auto";
                lambdaParameters.Add(cppType + " " + name);
                lambdaArguments.Add(AsCppValue(arguments[index]));
                var argumentIndex = index + (hasReceiver ? 1 : 0);
                helperArguments[argumentIndex] = helperIsByReference ? new QuestValue($"(&{name})", helperValueType.FullName, false, IsByRef: true) : new QuestValue(name, helperValueType.FullName, false);
            }

            var cppReturn = IlControlFlowLowerer.CppLocalType(effectiveReturn, method);
            var helperContext = new IlTranslationContext(method, context.Catalog, helperArguments, cppReturn, context.ActiveLocalMethods, genericArguments);
            var lowered = IlControlFlowLowerer.LowerRange(helperContext, 0, method.Body.Instructions.Count, includeLocalDeclarations: true);
            var body = IlControlFlowLowerer.RenderInlineHelper(lowered);
            var lambda = $"([&]({string.Join(", ", lambdaParameters)}) -> {cppReturn} {{\n{Indent(body)}\n}}({string.Join(", ", lambdaArguments)}))";
            var staticFields = helperContext.StaticFieldDeclarations.Select(pair => new QuestStaticField(pair.Key, pair.Value)).Concat(lowered.SelectMany(statement => statement.StaticFields ?? Array.Empty<QuestStaticField>())).DistinctBy(field => field.Name).ToArray();
            if (effectiveReturn.MetadataType == MetadataType.Void)
                return Raw(lambda + ";", call.FullName, staticFields: staticFields);

            stack.Push(new QuestValue(lambda, effectiveReturn.FullName, false));
            return null;
        }
        finally
        {
            context.ActiveLocalMethods.Remove(method.FullName);
        }
    }

    private static TypeReference ResolveType(TypeReference type, IReadOnlyDictionary<int, TypeReference> genericArguments)
    {
        if (type is GenericParameter parameter && parameter.Type == GenericParameterType.Method && genericArguments.TryGetValue(parameter.Position, out var argument))
            return argument;
        if (type is ArrayType array)
        {
            var elementType = ResolveType(array.ElementType, genericArguments);
            return array.IsVector ? new ArrayType(elementType) : new ArrayType(elementType, array.Rank);
        }
        if (type is ByReferenceType byReference)
            return new ByReferenceType(ResolveType(byReference.ElementType, genericArguments));
        if (type is GenericInstanceType generic)
        {
            var instance = new GenericInstanceType(generic.ElementType);
            foreach (var item in generic.GenericArguments)
                instance.GenericArguments.Add(ResolveType(item, genericArguments));
            return instance;
        }
        return type;
    }

    private static bool CanInlineHelperType(TypeReference type)
    {
        if (type is ByReferenceType byReference)
            return IsInlineValueType(byReference.ElementType) || byReference.ElementType.FullName == "System.String";
        if (type.FullName == "System.Void" || IsInlineValueType(type) || type.FullName == "System.String")
            return true;
        return QuestApiTypeMapper.Map(type) is not null && !type.FullName.StartsWith("System.Action", StringComparison.Ordinal) && !type.FullName.StartsWith("System.Func", StringComparison.Ordinal);
    }

    private static bool IsInlineValueType(TypeReference type) =>
        type.FullName
            is "System.Boolean"
                or "System.Byte"
                or "System.SByte"
                or "System.Int16"
                or "System.UInt16"
                or "System.Int32"
                or "System.UInt32"
                or "System.Int64"
                or "System.UInt64"
                or "System.Single"
                or "System.Double"
                or "UnityEngine.Color"
                or "UnityEngine.Vector2"
                or "UnityEngine.Vector3"
                or "UnityEngine.Vector4"
                or "UnityEngine.Quaternion"
        || type.Resolve()?.IsEnum == true;

    private static string Indent(string code) => string.Join("\n", code.Split('\n').Select(line => "    " + line));

    private static string AsCppValue(QuestValue value) =>
        value.PcType == "System.String" && value.IsStringLiteral
            ? "StringW(\"" + value.Code.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\")"
            : value.Code;

    private static bool TryBinary(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack)
    {
        if (
            instruction.OpCode.Code
            is not (
                Code.Add
                or Code.Add_Ovf
                or Code.Add_Ovf_Un
                or Code.Sub
                or Code.Sub_Ovf
                or Code.Sub_Ovf_Un
                or Code.Mul
                or Code.Mul_Ovf
                or Code.Mul_Ovf_Un
                or Code.Div
                or Code.Div_Un
                or Code.Rem
                or Code.Rem_Un
                or Code.And
                or Code.Or
                or Code.Xor
                or Code.Shl
                or Code.Shr
                or Code.Shr_Un
                or Code.Ceq
                or Code.Cgt
                or Code.Cgt_Un
                or Code.Clt
                or Code.Clt_Un
            )
        )
            return false;
        if (stack.Count < 2)
            throw new InvalidOperationException($"Evaluation stack underflow at IL_{instruction.Offset:x4}.");
        var right = stack.Pop();
        var left = stack.Pop();
        if (left.DeferredBoxedPrimitiveType is not null || right.DeferredBoxedPrimitiveType is not null)
            throw Unsupported(context.Method, instruction, "a deferred boxed primitive can only flow directly to the special logger path");
        if (left.IsByRef || right.IsByRef)
            throw Unsupported(context.Method, instruction, "arithmetic and comparison require values rather than by-reference stack entries");
        if (instruction.OpCode.Code is Code.Add_Ovf or Code.Add_Ovf_Un or Code.Sub_Ovf or Code.Sub_Ovf_Un or Code.Mul_Ovf or Code.Mul_Ovf_Un)
            throw Unsupported(context.Method, instruction, "checked overflow arithmetic is not emitted because native C++ overflow behavior does not match CLR overflow exceptions");
        if (instruction.OpCode.Code is Code.Div_Un or Code.Rem_Un or Code.Shr_Un && !IsUnsigned(left.PcType))
            throw Unsupported(context.Method, instruction, $"{instruction.OpCode.Code} requires an unsigned left operand; '{left.PcType}' has no unsigned C++ operation mapping");
        if (instruction.OpCode.Code is Code.Div_Un or Code.Rem_Un && !IsUnsigned(right.PcType))
            throw Unsupported(context.Method, instruction, $"{instruction.OpCode.Code} requires an unsigned right operand; '{right.PcType}' has no unsigned C++ operation mapping");
        var comparison = instruction.OpCode.Code is Code.Ceq or Code.Cgt or Code.Cgt_Un or Code.Clt or Code.Clt_Un;
        var referenceNullComparison = comparison && ((left.PcType == "System.Object" && right.PcType != "System.Object") || (right.PcType == "System.Object" && left.PcType != "System.Object"));
        var cliI4Compatible = left.PcType == "System.Boolean" && right.PcType == "System.Int32" || right.PcType == "System.Boolean" && left.PcType == "System.Int32";
        if (instruction.OpCode.Code is not (Code.Shl or Code.Shr or Code.Shr_Un) && left.PcType != right.PcType && !(comparison && cliI4Compatible) && !referenceNullComparison)
            throw Unsupported(context.Method, instruction, $"{instruction.OpCode.Code} requires matching operand types, received '{left.PcType}' and '{right.PcType}'");
        if (comparison)
        {
            var referenceValue = left.PcType == "System.Object" ? right : left;
            var comparisonCode = referenceNullComparison
                ? instruction.OpCode.Code == Code.Ceq
                    ? $"({referenceValue.Code} == nullptr)"
                    : $"({referenceValue.Code} != nullptr)"
                : IlComparisonLowerer.Format(left, right, instruction.OpCode.Code);
            stack.Push(new QuestValue(comparisonCode, "System.Boolean", false, left.ApiMapping ?? right.ApiMapping));
            return true;
        }
        var op = instruction.OpCode.Code switch
        {
            Code.Add or Code.Add_Ovf or Code.Add_Ovf_Un => "+",
            Code.Sub or Code.Sub_Ovf or Code.Sub_Ovf_Un => "-",
            Code.Mul or Code.Mul_Ovf or Code.Mul_Ovf_Un => "*",
            Code.Div or Code.Div_Un => "/",
            Code.Rem or Code.Rem_Un => "%",
            Code.And => IsBool(left.PcType) ? "&&" : "&",
            Code.Or => IsBool(left.PcType) ? "||" : "|",
            Code.Xor => "^",
            Code.Shl => "<<",
            Code.Shr or Code.Shr_Un => ">>",
            _ => "<",
        };
        var rightCode = instruction.OpCode.Code == Code.Shr_Un ? $"({right.Code} & {(left.PcType is "System.UInt64" or "System.UIntPtr" ? 63 : 31)})" : right.Code;
        stack.Push(new QuestValue($"({left.Code} {op} {rightCode})", left.PcType, false, left.ApiMapping ?? right.ApiMapping));
        return true;
    }

    private static bool TryUnary(Instruction instruction, Stack<QuestValue> stack)
    {
        if (instruction.OpCode.Code is not (Code.Neg or Code.Not))
            return false;
        if (stack.Count == 0)
            throw new InvalidOperationException($"Evaluation stack underflow at IL_{instruction.Offset:x4}.");
        var value = stack.Pop();
        if (value.DeferredBoxedPrimitiveType is not null)
            throw new InvalidOperationException($"Unsupported IL in deferred boxed primitive expression at IL_{instruction.Offset:x4}: only the special logger path can consume it.");
        stack.Push(new QuestValue(instruction.OpCode.Code == Code.Neg ? $"(-{value.Code})" : $"(~{value.Code})", value.PcType, false, value.ApiMapping));
        return true;
    }

    private static bool TryConvert(Instruction instruction, Stack<QuestValue> stack)
    {
        var target = instruction.OpCode.Code switch
        {
            Code.Conv_I1 => ("int8_t", "System.SByte"),
            Code.Conv_U1 => ("uint8_t", "System.Byte"),
            Code.Conv_I2 => ("int16_t", "System.Int16"),
            Code.Conv_U2 => ("uint16_t", "System.UInt16"),
            Code.Conv_I4 => ("int32_t", "System.Int32"),
            Code.Conv_U4 => ("uint32_t", "System.UInt32"),
            Code.Conv_I8 => ("int64_t", "System.Int64"),
            Code.Conv_U8 => ("uint64_t", "System.UInt64"),
            Code.Conv_R4 => ("float", "System.Single"),
            Code.Conv_R8 => ("double", "System.Double"),
            Code.Conv_I => ("intptr_t", "System.IntPtr"),
            Code.Conv_U => ("uintptr_t", "System.UIntPtr"),
            _ => ("", ""),
        };

        if (instruction.OpCode.Code == Code.Conv_R_Un)
        {
            if (stack.Count == 0)
                throw new InvalidOperationException($"Evaluation stack underflow at IL_{instruction.Offset:x4}.");
            var unsignedOperand = stack.Pop();
            if (!TryUnsignedCppType(unsignedOperand.PcType, out var unsignedType))
                return false;
            stack.Push(new QuestValue($"static_cast<double>(static_cast<{unsignedType}>({unsignedOperand.Code}))", "System.Double", false, unsignedOperand.ApiMapping));
            return true;
        }

        if (target.Item1.Length == 0)
            return false;
        if (stack.Count == 0)
            throw new InvalidOperationException($"Evaluation stack underflow at IL_{instruction.Offset:x4}.");
        var operand = stack.Pop();
        if (operand.DeferredBoxedPrimitiveType is not null)
            throw new InvalidOperationException($"Unsupported IL in deferred boxed primitive expression at IL_{instruction.Offset:x4}: only the special logger path can consume it.");
        if (!IsNumeric(operand.PcType))
            return false;
        stack.Push(new QuestValue($"static_cast<{target.Item1}>({operand.Code})", target.Item2, false, operand.ApiMapping));
        return true;
    }

    private static bool TryUnsignedCppType(string pcType, out string cppType)
    {
        cppType = pcType switch
        {
            "System.SByte" or "System.Byte" => "uint8_t",
            "System.Int16" or "System.UInt16" => "uint16_t",
            "System.Int32" or "System.UInt32" => "uint32_t",
            "System.Int64" or "System.UInt64" => "uint64_t",
            "System.IntPtr" or "System.UIntPtr" => "uintptr_t",
            _ => "",
        };
        return cppType.Length > 0;
    }

    private static bool IsNumeric(string pcType) => pcType is "System.SByte" or "System.Byte" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" or "System.Single" or "System.Double" or "System.IntPtr" or "System.UIntPtr";

    private static bool IsUnsigned(string pcType) => pcType is "System.Byte" or "System.UInt16" or "System.UInt32" or "System.UInt64" or "System.UIntPtr";

    private static bool TryNumericConstant(Instruction instruction, out QuestValue value)
    {
        var code = instruction.OpCode.Code;
        if (code is >= Code.Ldc_I4_M1 and <= Code.Ldc_I4_8 || code is Code.Ldc_I4 or Code.Ldc_I4_S)
        {
            var number = code switch
            {
                Code.Ldc_I4_M1 => -1,
                >= Code.Ldc_I4_0 and <= Code.Ldc_I4_8 => (int)code - (int)Code.Ldc_I4_0,
                Code.Ldc_I4_S => (sbyte)instruction.Operand,
                _ => (int)instruction.Operand,
            };
            value = new QuestValue(number.ToString(System.Globalization.CultureInfo.InvariantCulture), "System.Int32", false);
            return true;
        }
        if (code == Code.Ldc_I8)
        {
            value = new QuestValue(((long)instruction.Operand).ToString(System.Globalization.CultureInfo.InvariantCulture), "System.Int64", false);
            return true;
        }
        if (code == Code.Ldc_R4)
        {
            var literal = ((float)instruction.Operand).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (!literal.Contains('.') && !literal.Contains('E') && !literal.Contains('e'))
                literal += ".0";
            value = new QuestValue(literal + "f", "System.Single", false);
            return true;
        }
        if (code == Code.Ldc_R8)
        {
            value = new QuestValue(((double)instruction.Operand).ToString("R", System.Globalization.CultureInfo.InvariantCulture), "System.Double", false);
            return true;
        }
        value = null!;
        return false;
    }

    private static bool TryGetLocalIndex(Instruction instruction, bool load, out int index)
    {
        var code = instruction.OpCode.Code;
        var first = load ? Code.Ldloc_0 : Code.Stloc_0;
        if (code >= first && code <= first + 3)
        {
            index = (int)code - (int)first;
            return true;
        }
        if (code == (load ? Code.Ldloc : Code.Stloc) || code == (load ? Code.Ldloc_S : Code.Stloc_S))
        {
            index = ((VariableDefinition)instruction.Operand).Index;
            return true;
        }
        index = -1;
        return false;
    }

    private static QuestValue Pop(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack) => stack.Count == 0 ? throw Unsupported(context.Method, instruction) : stack.Pop();

    private static void RequireStack(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack, int expected)
    {
        if (stack.Count != expected)
            throw Unsupported(context.Method, instruction);
    }

    private static bool IsBool(string type) => type == "System.Boolean";

    private static bool IsSameModAssembly(ModuleDefinition module, string scopeName) => scopeName == module.Name || scopeName == module.Assembly.Name.Name;

    private static QuestLogStatement Raw(string code, string source, ApiCallMapping? mapping = null, IReadOnlyList<QuestStaticField>? staticFields = null) => new("", "", source, null, mapping, code, StaticFields: staticFields);

    private static InvalidOperationException Unsupported(MethodDefinition method, Instruction instruction) => new($"Unsupported IL in {method.FullName} at IL_{instruction.Offset:x4}: {instruction.OpCode.Code}. The Quest backend does not guess at runtime/API behavior.");

    private static InvalidOperationException Unsupported(MethodDefinition method, Instruction instruction, string reason) => new($"Unsupported IL in {method.FullName} at IL_{instruction.Offset:x4}: {instruction.OpCode.Code}: {reason}.");

    private static bool TryGetLoggingLevel(MethodReference call, out string level)
    {
        level = "";
        if (call.HasThis || call.ReturnType.MetadataType != MetadataType.Void || call.Parameters.Count != 1)
            return false;
        if (call.DeclaringType.FullName == "UnityEngine.Debug" && call.DeclaringType.Scope.Name == "UnityEngine.CoreModule" && call.Parameters[0].ParameterType.FullName == "System.Object")
        {
            level = call.Name switch
            {
                "Log" => "info",
                "LogWarning" => "warn",
                "LogError" => "error",
                _ => "",
            };
            return level.Length > 0;
        }
        if (call.DeclaringType.FullName == "System.Console" && call.Name == "WriteLine" && call.Parameters[0].ParameterType.FullName == "System.String")
        {
            level = "info";
            return true;
        }
        return false;
    }
}
