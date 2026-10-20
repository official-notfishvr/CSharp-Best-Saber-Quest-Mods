using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Transpiler;

internal sealed partial class Transpiler
{
    private List<PatchEmission> BuildPatchEmissions(Dictionary<PatchDefinition, IlMethodTranslator> bodyGenerators)
    {
        var orderedHooks = _patches.Select((hook, index) => (hook, index)).ToList();
        var grouped = orderedHooks.GroupBy(item => BuildHookGroupKey(item.hook)).OrderBy(group => group.Min(item => item.index));

        var emissions = new List<PatchEmission>();

        foreach (var group in grouped)
        {
            var hooks = group.Select(item => item.hook).ToList();
            var fullHooks = hooks.Where(hook => hook.Phase == PatchBehavior.Full).ToList();
            var prefixHooks = hooks.Where(hook => hook.Phase == PatchBehavior.Prefix).ToList();
            var postfixHooks = hooks.Where(hook => hook.Phase == PatchBehavior.Postfix).ToList();

            if (fullHooks.Count > 0)
            {
                if (prefixHooks.Count > 0 || postfixHooks.Count > 0)
                    throw new InvalidOperationException($"Patch group for {hooks[0].TargetType.FullName}.{hooks[0].TargetMethod} mixes full hooks with prefix/postfix hooks.");

                foreach (var hook in fullHooks)
                    emissions.Add(new PatchEmission(hook, null, null, bodyGenerators[hook], null, null));

                continue;
            }

            if (prefixHooks.Count > 1)
                throw new InvalidOperationException($"Patch group for {hooks[0].TargetType.FullName}.{hooks[0].TargetMethod} has multiple before patches.");
            if (postfixHooks.Count > 1)
                throw new InvalidOperationException($"Patch group for {hooks[0].TargetType.FullName}.{hooks[0].TargetMethod} has multiple after patches.");

            var prefixHook = prefixHooks.SingleOrDefault();
            var postfixHook = postfixHooks.SingleOrDefault();
            if (prefixHook == null && postfixHook == null)
                continue;

            if (prefixHook != null && postfixHook != null)
            {
                EnsureHookSignatureMatch(prefixHook, postfixHook);
                if (!string.Equals(prefixHook.HookName, postfixHook.HookName, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Patch group for {hooks[0].TargetType.FullName}.{hooks[0].TargetMethod} has mismatched hook names ({prefixHook.HookName} vs {postfixHook.HookName}).");
            }

            var signatureHook = prefixHook?.Method.ReturnType.FullName == "System.Boolean" ? postfixHook ?? prefixHook : prefixHook ?? postfixHook!;
            var targetReturnType = ResolveTargetMethodReturnType(signatureHook) ?? throw new InvalidOperationException($"Could not resolve the target method signature for hook {signatureHook.Method.FullName}.");
            signatureHook = new PatchDefinition
            {
                HookName = signatureHook.HookName,
                TargetMethod = signatureHook.TargetMethod,
                TargetType = signatureHook.TargetType,
                Method = signatureHook.Method,
                IsConstructor = signatureHook.IsConstructor,
                Phase = signatureHook.Phase,
                RuntimeReturnType = targetReturnType,
            };

            emissions.Add(new PatchEmission(signatureHook, prefixHook, postfixHook, null, prefixHook != null ? bodyGenerators[prefixHook] : null, postfixHook != null ? bodyGenerators[postfixHook] : null));
        }

        return emissions;
    }

    private TypeReference? ResolveTargetMethodReturnType(PatchDefinition hook)
    {
        var targetMethod = ResolveTargetMethodDefinition(hook);
        return targetMethod == null ? null : _module?.ImportReference(targetMethod.ReturnType) ?? targetMethod.ReturnType;
    }

    private MethodDefinition? ResolveTargetMethodDefinition(PatchDefinition hook)
    {
        try
        {
            var resolved = hook.TargetType.Resolve();
            while (resolved != null)
            {
                var targetMethod = resolved.Methods.FirstOrDefault(method =>
                    string.Equals(method.Name, hook.TargetMethod, StringComparison.Ordinal)
                    && method.Parameters.Count == hook.Method.Parameters.Count - 1
                    && method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(hook.Method.Parameters.Skip(1).Select(parameter => parameter.ParameterType.FullName), StringComparer.Ordinal)
                );
                if (targetMethod != null)
                    return targetMethod;

                resolved = resolved.BaseType?.Resolve();
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Could not inspect the generated binding for {hook.TargetType.FullName}.{hook.TargetMethod}: {exception.Message}", exception);
        }

        return null;
    }

    private void ValidateHookTargetSignatures()
    {
        foreach (var hook in _patches)
        {
            var targetMethod = ResolveTargetMethodDefinition(hook);
            if (targetMethod == null)
            {
                var parameters = string.Join(", ", hook.Method.Parameters.Skip(1).Select(parameter => parameter.ParameterType.FullName));
                throw new InvalidOperationException($"No typed binding was found for {hook.TargetType.FullName}.{hook.TargetMethod}({parameters}). Check the target type, method name, overload parameters, and generated game bindings.");
            }

            if (!IsTargetAssignableToHookSelf(hook.TargetType, hook.Method.Parameters[0].ParameterType))
                throw new InvalidOperationException($"Patch target parameter {hook.Method.Parameters[0].ParameterType.FullName} must be the target type or one of its base types ({hook.TargetType.FullName}).");

            if (hook.Phase == PatchBehavior.Full && hook.Method.ReturnType.FullName != targetMethod.ReturnType.FullName)
                throw new InvalidOperationException($"Replacement patch {hook.Method.FullName} returns {hook.Method.ReturnType.FullName}, but target {targetMethod.FullName} returns {targetMethod.ReturnType.FullName}.");
        }
    }

    private static bool IsTargetAssignableToHookSelf(TypeReference targetType, TypeReference hookSelfType)
    {
        try
        {
            for (var current = targetType.Resolve(); current != null; current = current.BaseType?.Resolve())
            {
                if (string.Equals(current.FullName, hookSelfType.FullName, StringComparison.Ordinal))
                    return true;
            }
        }
        catch { }

        return string.Equals(targetType.FullName, hookSelfType.FullName, StringComparison.Ordinal);
    }

    private static string BuildHookGroupKey(PatchDefinition hook)
    {
        var parameterKey = string.Join("|", hook.Method.Parameters.Select(parameter => parameter.ParameterType.FullName));
        return $"{hook.TargetType.FullName}|{hook.TargetMethod}|{hook.IsConstructor}|{parameterKey}";
    }

    private static void EnsureHookSignatureMatch(PatchDefinition left, PatchDefinition right)
    {
        var prefixReturnControl =
            left.Phase == PatchBehavior.Prefix && left.Method.ReturnType.FullName == "System.Boolean" && right.Phase == PatchBehavior.Postfix && right.Method.ReturnType.FullName == "System.Void"
            || right.Phase == PatchBehavior.Prefix && right.Method.ReturnType.FullName == "System.Boolean" && left.Phase == PatchBehavior.Postfix && left.Method.ReturnType.FullName == "System.Void";
        if (!prefixReturnControl && !string.Equals(left.Method.ReturnType.FullName, right.Method.ReturnType.FullName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Patch methods {left.Method.FullName} and {right.Method.FullName} must return the same type.");

        if (left.Method.Parameters.Count != right.Method.Parameters.Count)
            throw new InvalidOperationException($"Patch methods {left.Method.FullName} and {right.Method.FullName} must have the same parameter count.");

        for (var i = 0; i < left.Method.Parameters.Count; i++)
        {
            var leftParam = left.Method.Parameters[i];
            var rightParam = right.Method.Parameters[i];
            if (!string.Equals(leftParam.ParameterType.FullName, rightParam.ParameterType.FullName, StringComparison.Ordinal))
                throw new InvalidOperationException($"Patch methods {left.Method.FullName} and {right.Method.FullName} must have matching parameter types.");
        }
    }
}
