using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal static class IlReflectionLowerer
{
    private const string TypeMarkerPrefix = "__il_type:";

    public static bool TryLower(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack, out QuestLogStatement? statement)
    {
        statement = null;
        switch (instruction.OpCode.Code)
        {
            case Code.Ldtoken when instruction.Operand is TypeReference type:
                stack.Push(new QuestValue(TypeMarkerPrefix + type.FullName, "System.RuntimeTypeHandle", false));
                return true;
            case Code.Castclass when instruction.Operand is TypeReference castType:
            {
                var value = Pop(context, instruction, stack);
                if (value.PcType != castType.FullName)
                    throw Unsupported(context, instruction, $"castclass requires a statically proven reference type; received '{value.PcType}', requested '{castType.FullName}'");
                stack.Push(value);
                return true;
            }
            default:
                return false;
        }
    }

    public static bool TryLowerCall(IlTranslationContext context, Instruction instruction, MethodReference call, Stack<QuestValue> stack, out QuestLogStatement? statement)
    {
        statement = null;
        if (call.DeclaringType.FullName == "System.Type" && call.Name == "GetTypeFromHandle" && !call.HasThis && call.Parameters.Count == 1 && call.ReturnType.FullName == "System.Type" && call.Parameters[0].ParameterType.FullName == "System.RuntimeTypeHandle")
        {
            var handle = Pop(context, instruction, stack);
            if (handle.PcType != "System.RuntimeTypeHandle" || !handle.Code.StartsWith(TypeMarkerPrefix, StringComparison.Ordinal))
                throw Unsupported(context, instruction, "GetTypeFromHandle requires an ldtoken type operand");
            stack.Push(new QuestValue(handle.Code[TypeMarkerPrefix.Length..], "System.Type", false));
            return true;
        }

        if (
            call.DeclaringType.FullName == "System.Type"
            && call.Name == "GetField"
            && call.HasThis
            && call.Parameters.Count == 2
            && call.ReturnType.FullName == "System.Reflection.FieldInfo"
            && call.Parameters[0].ParameterType.FullName == "System.String"
            && call.Parameters[1].ParameterType.FullName == "System.Reflection.BindingFlags"
        )
        {
            var flags = Pop(context, instruction, stack);
            var name = Pop(context, instruction, stack);
            var typeValue = Pop(context, instruction, stack);
            if (typeValue.PcType != "System.Type" || name.PcType != "System.String" || !name.IsStringLiteral || !int.TryParse(flags.Code, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var bindingFlags))
                throw Unsupported(context, instruction, "GetField requires an ldtoken-derived type, constant field name, and constant BindingFlags");

            var definition = ResolveType(context.Method.Module, typeValue.Code) ?? throw Unsupported(context, instruction, $"GetField could not resolve metadata for '{typeValue.Code}'");
            var field = FindField(definition, name.Code, bindingFlags) ?? throw Unsupported(context, instruction, $"GetField could not resolve a unique instance field '{name.Code}' on '{typeValue.Code}'");
            stack.Push(new QuestValue(field.FullName, "System.Reflection.FieldInfo", false));
            return true;
        }

        if (call.DeclaringType.FullName == "System.Reflection.FieldInfo" && call.Name == "GetValue" && call.HasThis && call.Parameters.Count == 1 && call.ReturnType.FullName == "System.Object" && call.Parameters[0].ParameterType.FullName == "System.Object")
        {
            var receiver = Pop(context, instruction, stack);
            var fieldInfo = Pop(context, instruction, stack);
            if (fieldInfo.PcType != "System.Reflection.FieldInfo" || !TryResolveField(context.Method.Module, fieldInfo.Code, out var field) || field.IsStatic || field.FieldType.IsValueType || receiver.IsByRef || receiver.PcType != field.DeclaringType.FullName)
                throw Unsupported(context, instruction, "FieldInfo.GetValue is supported for resolved instance reference fields on a statically matching receiver");
            stack.Push(new QuestValue($"{receiver.Code}->___{field.Name}", field.FieldType.FullName, false));
            return true;
        }

        return false;
    }

    private static FieldDefinition? FindField(TypeDefinition type, string name, int flags)
    {
        const int Instance = 4;
        const int Static = 8;
        const int Public = 16;
        const int NonPublic = 32;
        if ((flags & (Instance | Static)) == 0 || (flags & (Public | NonPublic)) == 0)
            return null;

        for (var current = type; current is not null; current = current.BaseType?.Resolve())
        {
            var candidates = current
                .Fields.Where(field => field.Name == name && ((field.IsStatic && (flags & Static) != 0) || (!field.IsStatic && (flags & Instance) != 0)) && (current == type || !field.IsPrivate) && ((field.IsPublic && (flags & Public) != 0) || (!field.IsPublic && (flags & NonPublic) != 0)))
                .ToArray();
            if (candidates.Length > 1)
                return null;
            if (candidates.Length == 1)
                return candidates[0];
        }
        return null;
    }

    private static TypeDefinition? ResolveType(ModuleDefinition module, string fullName)
    {
        var local = module.GetType(fullName);
        if (local is not null)
            return local;
        foreach (var reference in module.GetTypeReferences().Where(reference => reference.FullName == fullName))
        {
            try
            {
                var resolved = reference.Resolve();
                if (resolved is not null)
                    return resolved;
            }
            catch (AssemblyResolutionException) { }
        }
        return null;
    }

    private static bool TryResolveField(ModuleDefinition module, string fullName, out FieldDefinition field)
    {
        foreach (var type in module.GetTypeReferences().Select(reference => ResolveType(module, reference.FullName)).Where(type => type is not null).Cast<TypeDefinition>().DistinctBy(type => type.FullName))
        {
            var match = type.Fields.FirstOrDefault(candidate => candidate.FullName == fullName);
            if (match is not null)
            {
                field = match;
                return true;
            }
        }
        field = null!;
        return false;
    }

    private static QuestValue Pop(IlTranslationContext context, Instruction instruction, Stack<QuestValue> stack) => stack.Count == 0 ? throw Unsupported(context, instruction, "evaluation stack underflow") : stack.Pop();

    private static InvalidOperationException Unsupported(IlTranslationContext context, Instruction instruction, string reason) => new($"Unsupported IL in {context.Method.FullName} at IL_{instruction.Offset:x4} ({instruction.OpCode.Code}): {reason}.");
}
