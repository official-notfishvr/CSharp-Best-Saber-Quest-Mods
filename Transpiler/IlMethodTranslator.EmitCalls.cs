using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed partial class IlMethodTranslator
{
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

        if (TryBuildBsmlLiteIntrinsic(method, args, indentLevel, out var bsmlLiteExpression))
        {
            if (method.ReturnType.FullName == "System.Void")
                AppendLine(indentLevel, $"{bsmlLiteExpression.Code};");
            else
                _stack.Push(bsmlLiteExpression);

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
}
