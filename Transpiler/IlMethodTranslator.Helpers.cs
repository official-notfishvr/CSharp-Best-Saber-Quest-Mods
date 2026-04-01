using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed partial class IlMethodTranslator
{
    private TranslationSnapshot CaptureSnapshot()
    {
        return new TranslationSnapshot(_stack.ToArray(), Statements.Count, _declaredLocals.OrderBy(index => index).ToArray());
    }

    private void RestoreSnapshot(TranslationSnapshot snapshot)
    {
        _stack.Clear();
        for (var index = snapshot.StackValues.Length - 1; index >= 0; index--)
            _stack.Push(snapshot.StackValues[index]);

        if (Statements.Count > snapshot.LineCount)
            Statements.RemoveRange(snapshot.LineCount, Statements.Count - snapshot.LineCount);

        _declaredLocals.Clear();
        foreach (var localIndex in snapshot.DeclaredLocals)
            _declaredLocals.Add(localIndex);
    }

    private CppExpression Pop()
    {
        if (_stack.Count == 0)
            throw new InvalidOperationException($"IL stack underflow while translating {_method.FullName}");

        return _stack.Pop();
    }

    private string GetLocalName(int index)
    {
        return _localNames.TryGetValue(index, out var name) ? name : $"local{index}";
    }

    private string GetArgumentName(int index)
    {
        return _parameterNames.TryGetValue(index, out var name) ? name : $"arg{index}";
    }

    private static string BuildConfigAccessorKey(string declaringTypeFullName, string memberName)
    {
        return $"{declaringTypeFullName}::{memberName}";
    }

    private void RequiredInclude(TypeReference? type)
    {
        var include = _typeSystem.GetIncludePath(type);
        if (include != null)
            RequiredIncludes.Add(include);
    }

    private void AppendLine(int indentLevel, string line = "")
    {
        Statements.Add($"{new string(' ', indentLevel * 4)}{line}");
    }

    private static string GetMemberAccessOperator(TypeReference? type)
    {
        if (type == null)
            return "->";

        if (type is ByReferenceType byReferenceType)
            return IsValueType(byReferenceType.ElementType) ? "." : "->";

        return IsValueType(type) ? "." : "->";
    }

    private static bool IsValueType(TypeReference? type)
    {
        if (type == null)
            return false;

        if (type is ByReferenceType byReferenceType)
            return IsValueType(byReferenceType.ElementType);

        return type.IsValueType || type.Resolve()?.IsValueType == true;
    }

    private static bool IsBooleanType(TypeReference? type)
    {
        if (type == null)
            return false;

        if (type is ByReferenceType byReferenceType)
            return IsBooleanType(byReferenceType.ElementType);

        return type.FullName == "System.Boolean";
    }

    private static bool TryGetLocalIndex(Instruction instruction, out int localIndex)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Stloc_0:
                localIndex = 0;
                return true;
            case Code.Stloc_1:
                localIndex = 1;
                return true;
            case Code.Stloc_2:
                localIndex = 2;
                return true;
            case Code.Stloc_3:
                localIndex = 3;
                return true;
            case Code.Stloc:
            case Code.Stloc_S:
                localIndex = ((VariableDefinition)instruction.Operand).Index;
                return true;
            default:
                localIndex = -1;
                return false;
        }
    }

    private static bool TryGetLoadedLocalIndex(Instruction instruction, out int localIndex)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Ldloc_0:
                localIndex = 0;
                return true;
            case Code.Ldloc_1:
                localIndex = 1;
                return true;
            case Code.Ldloc_2:
                localIndex = 2;
                return true;
            case Code.Ldloc_3:
                localIndex = 3;
                return true;
            case Code.Ldloc:
            case Code.Ldloc_S:
                localIndex = ((VariableDefinition)instruction.Operand).Index;
                return true;
            default:
                localIndex = -1;
                return false;
        }
    }

    private static bool TryGetLoadedArgumentIndex(Instruction instruction, out int argumentIndex)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Ldarg_0:
                argumentIndex = 0;
                return true;
            case Code.Ldarg_1:
                argumentIndex = 1;
                return true;
            case Code.Ldarg_2:
                argumentIndex = 2;
                return true;
            case Code.Ldarg_3:
                argumentIndex = 3;
                return true;
            case Code.Ldarg:
            case Code.Ldarg_S:
                argumentIndex = ((ParameterDefinition)instruction.Operand).Index;
                return true;
            default:
                argumentIndex = -1;
                return false;
        }
    }

    private static Dictionary<int, string> BuildLocalNameMap(MethodDefinition method)
    {
        var map = new Dictionary<int, string>();
        VisitScope(method.DebugInformation.Scope, map);
        return map;
    }

    private static void VisitScope(ScopeDebugInformation? scope, IDictionary<int, string> map)
    {
        if (scope == null)
            return;

        foreach (var variable in scope.Variables)
        {
            if (!string.IsNullOrWhiteSpace(variable.Name))
                map[variable.Index] = CppIdentifier.Sanitize(variable.Name, $"local{variable.Index}");
        }

        foreach (var nestedScope in scope.Scopes)
            VisitScope(nestedScope, map);
    }
}
