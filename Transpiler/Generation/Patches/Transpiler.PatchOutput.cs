using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Transpiler;

internal sealed partial class Transpiler
{
    private void WriteHook(CppCodeWriter writer, PatchDefinition hook, IlMethodTranslator bodyGenerator)
    {
        var runtimeTargetType = ResolveRuntimeHookTargetType(hook);
        var runtimeReturnType = hook.RuntimeReturnType ?? hook.Method.ReturnType;
        var returnType = _typeSystem.MapType(runtimeReturnType);
        var parameters = BuildHookRuntimeParameters(hook, runtimeTargetType);

        writer.WriteLine("MAKE_HOOK_MATCH(");
        writer.WriteLine($"    {hook.HookName},");
        writer.WriteLine($"    &{_typeSystem.MapNamespace(runtimeTargetType.Namespace)}::{_typeSystem.ComposeTypeName(runtimeTargetType)}::{MapTargetMethodName(hook)},");
        writer.WriteLine($"    {returnType},");
        writer.WriteLine($"    {string.Join(", ", parameters)}) {{");

        if (RequiresHookSelfRuntimeCheck(hook, runtimeTargetType))
        {
            writer.WriteLine($"    if (!({BuildHookSelfMatchesExpression(hook, runtimeTargetType)})) {{");
            WriteOriginalFallback(writer, hook, runtimeTargetType, 2);
            writer.WriteLine("    }");
        }

        WriteHookParameterCasts(writer, hook, runtimeTargetType, defineMatchFlag: false);

        foreach (var line in bodyGenerator.Statements)
            writer.WriteLine($"    {line}");

        writer.WriteLine("}");
        writer.WriteLine();
    }

    private void WriteHookWithPrefixPostfix(CppCodeWriter writer, PatchDefinition hook, PatchDefinition? prefixHook, IlMethodTranslator? prefixBody, PatchDefinition? postfixHook, IlMethodTranslator? postfixBody)
    {
        if (prefixHook == null && postfixHook == null)
            throw new InvalidOperationException($"Hook {hook.HookName} must have a prefix or postfix method.");

        var runtimeTargetType = ResolveRuntimeHookTargetType(hook);
        var runtimeReturnType = hook.RuntimeReturnType ?? hook.Method.ReturnType;
        var returnType = _typeSystem.MapType(runtimeReturnType);
        var parameters = BuildHookRuntimeParameters(hook, runtimeTargetType);
        var argumentList = string.Join(", ", hook.Method.Parameters.Select(parameter => CppIdentifier.Sanitize(parameter.Name)));
        var runtimeArgumentList = BuildHookRuntimeArgumentList(hook, runtimeTargetType);

        if (prefixHook != null && prefixBody != null)
            WriteHelperFunction(writer, prefixHook, prefixBody);

        if (postfixHook != null && postfixBody != null)
            WriteHelperFunction(writer, postfixHook, postfixBody);

        writer.WriteLine("MAKE_HOOK_MATCH(");
        writer.WriteLine($"    {hook.HookName},");
        writer.WriteLine($"    &{_typeSystem.MapNamespace(runtimeTargetType.Namespace)}::{_typeSystem.ComposeTypeName(runtimeTargetType)}::{MapTargetMethodName(hook)},");
        writer.WriteLine($"    {returnType},");
        writer.WriteLine($"    {string.Join(", ", parameters)}) {{");

        var hasRuntimeSelfCheck = RequiresHookSelfRuntimeCheck(hook, runtimeTargetType);
        WriteHookParameterCasts(writer, hook, runtimeTargetType, defineMatchFlag: hasRuntimeSelfCheck);
        var prefixControlsOriginal = prefixHook?.Method.ReturnType.FullName == "System.Boolean";

        if (prefixHook != null && !prefixControlsOriginal)
        {
            if (hasRuntimeSelfCheck)
                writer.WriteLine($"    if (hookSelfMatches) {GetHelperFunctionName(prefixHook)}({argumentList});");
            else
                writer.WriteLine($"    {GetHelperFunctionName(prefixHook)}({argumentList});");
        }

        if (prefixControlsOriginal)
        {
            writer.WriteLine("    auto shouldRunOriginal = true;");
            var prefixCall = $"{GetHelperFunctionName(prefixHook!)}({argumentList})";
            if (hasRuntimeSelfCheck)
                writer.WriteLine($"    if (hookSelfMatches) shouldRunOriginal = {prefixCall};");
            else
                writer.WriteLine($"    shouldRunOriginal = {prefixCall};");
        }

        if (runtimeReturnType.FullName == "System.Void")
        {
            if (prefixControlsOriginal)
            {
                writer.WriteLine("    if (shouldRunOriginal) {");
                writer.WriteLine($"        {hook.HookName}({runtimeArgumentList});");
                writer.WriteLine("    }");
            }
            else
            {
                writer.WriteLine($"    {hook.HookName}({runtimeArgumentList});");
            }
            if (postfixHook != null)
            {
                if (hasRuntimeSelfCheck)
                    writer.WriteLine($"    if (hookSelfMatches) {GetHelperFunctionName(postfixHook)}({argumentList});");
                else
                    writer.WriteLine($"    {GetHelperFunctionName(postfixHook)}({argumentList});");
            }
            writer.WriteLine("    return;");
        }
        else
        {
            if (prefixControlsOriginal)
            {
                writer.WriteLine($"    auto result = {returnType}{{}};");
                writer.WriteLine($"    if (shouldRunOriginal) result = {hook.HookName}({runtimeArgumentList});");
            }
            else
            {
                writer.WriteLine($"    auto result = {hook.HookName}({runtimeArgumentList});");
            }
            if (postfixHook != null)
            {
                if (hasRuntimeSelfCheck)
                    writer.WriteLine($"    if (hookSelfMatches) {GetHelperFunctionName(postfixHook)}({argumentList});");
                else
                    writer.WriteLine($"    {GetHelperFunctionName(postfixHook)}({argumentList});");
            }
            writer.WriteLine("    return result;");
        }

        writer.WriteLine("}");
        writer.WriteLine();
    }

    private List<string> BuildHookRuntimeParameters(PatchDefinition hook, TypeReference runtimeTargetType)
    {
        var parameters = new List<string>();
        for (var i = 0; i < hook.Method.Parameters.Count; i++)
        {
            var parameter = hook.Method.Parameters[i];
            var parameterType = i == 0 ? runtimeTargetType : parameter.ParameterType;
            var parameterName = i == 0 ? GetHookRuntimeParameterName(hook, runtimeTargetType) : CppIdentifier.Sanitize(parameter.Name);
            parameters.Add($"{_typeSystem.MapType(parameterType)} {parameterName}");
        }

        return parameters;
    }

    private string BuildHookRuntimeArgumentList(PatchDefinition hook, TypeReference runtimeTargetType)
    {
        return string.Join(", ", hook.Method.Parameters.Select((parameter, index) => index == 0 ? GetHookRuntimeParameterName(hook, runtimeTargetType) : CppIdentifier.Sanitize(parameter.Name)));
    }

    private static string GetHookRuntimeParameterName(PatchDefinition hook, TypeReference runtimeTargetType)
    {
        var selfName = CppIdentifier.Sanitize(hook.Method.Parameters[0].Name);
        return string.Equals(hook.Method.Parameters[0].ParameterType.FullName, runtimeTargetType.FullName, StringComparison.Ordinal) ? selfName : $"{selfName}Raw";
    }

    private void WriteHookParameterCasts(CppCodeWriter writer, PatchDefinition hook, TypeReference runtimeTargetType, bool defineMatchFlag)
    {
        if (hook.Method.Parameters.Count == 0)
            return;

        var expectedSelfType = hook.Method.Parameters[0].ParameterType;
        if (string.Equals(expectedSelfType.FullName, runtimeTargetType.FullName, StringComparison.Ordinal))
            return;

        var selfName = CppIdentifier.Sanitize(hook.Method.Parameters[0].Name);
        var runtimeSelfName = GetHookRuntimeParameterName(hook, runtimeTargetType);
        if (defineMatchFlag)
        {
            writer.WriteLine($"    auto hookSelfMatches = {BuildHookSelfMatchesExpression(hook, runtimeTargetType)};");
            writer.WriteLine($"    auto {selfName} = hookSelfMatches ? reinterpret_cast<{_typeSystem.MapType(expectedSelfType)}>({runtimeSelfName}) : nullptr;");
        }
        else
        {
            writer.WriteLine($"    auto {selfName} = reinterpret_cast<{_typeSystem.MapType(expectedSelfType)}>({runtimeSelfName});");
        }
    }

    private bool RequiresHookSelfRuntimeCheck(PatchDefinition hook, TypeReference runtimeTargetType)
    {
        return hook.Method.Parameters.Count > 0 && !string.Equals(hook.Method.Parameters[0].ParameterType.FullName, runtimeTargetType.FullName, StringComparison.Ordinal);
    }

    private string BuildHookSelfMatchesExpression(PatchDefinition hook, TypeReference runtimeTargetType)
    {
        var expectedSelfType = hook.Method.Parameters[0].ParameterType;
        var runtimeSelfName = GetHookRuntimeParameterName(hook, runtimeTargetType);
        return $"{runtimeSelfName} != nullptr && ::il2cpp_functions::class_is_assignable_from(classof({_typeSystem.MapType(expectedSelfType)}), reinterpret_cast<Il2CppObject*>({runtimeSelfName})->klass)";
    }

    private void WriteOriginalFallback(CppCodeWriter writer, PatchDefinition hook, TypeReference runtimeTargetType, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 4);
        var runtimeArgumentList = BuildHookRuntimeArgumentList(hook, runtimeTargetType);
        var runtimeReturnType = hook.RuntimeReturnType ?? hook.Method.ReturnType;
        if (runtimeReturnType.FullName == "System.Void")
        {
            writer.WriteLine($"{indent}{hook.HookName}({runtimeArgumentList});");
            writer.WriteLine($"{indent}return;");
        }
        else
        {
            writer.WriteLine($"{indent}return {hook.HookName}({runtimeArgumentList});");
        }
    }

    private TypeReference ResolveRuntimeHookTargetType(PatchDefinition hook)
    {
        if (hook.IsConstructor)
            return hook.TargetType;

        var targetMethod = ResolveTargetMethodDefinition(hook) ?? throw new InvalidOperationException($"Could not resolve a target method with the exact parameter types for {hook.Method.FullName}.");
        return _module?.ImportReference(targetMethod.DeclaringType) ?? targetMethod.DeclaringType;
    }

    private void WriteHelperFunction(CppCodeWriter writer, PatchDefinition hook, IlMethodTranslator bodyGenerator)
    {
        var returnType = _typeSystem.MapType(hook.Method.ReturnType);
        var parameters = hook.Method.Parameters.Select(parameter => $"{_typeSystem.MapType(parameter.ParameterType)} {CppIdentifier.Sanitize(parameter.Name)}").ToList();

        writer.WriteLine($"static {returnType} {GetHelperFunctionName(hook)}({string.Join(", ", parameters)}) {{");
        foreach (var line in bodyGenerator.Statements)
            writer.WriteLine($"    {line}");
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private void WriteHelperPrototype(CppCodeWriter writer, HelperMethodEmission helperMethod)
    {
        var returnType = _typeSystem.MapType(helperMethod.Method.ReturnType);
        var parameters = helperMethod.Method.Parameters.Select(parameter => $"{_typeSystem.MapType(parameter.ParameterType)} {CppIdentifier.Sanitize(parameter.Name)}").ToList();
        writer.WriteLine($"static {returnType} {helperMethod.FunctionName}({string.Join(", ", parameters)});");
    }
}
