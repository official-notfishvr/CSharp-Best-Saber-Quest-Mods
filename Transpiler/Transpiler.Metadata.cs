using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed partial class Transpiler
{
    private void LoadLocalStaticFields(TypeDefinition type)
    {
        if (!IsTranspilerRelevantType(type))
            return;

        var defaults = ReadStaticDefaults(type);

        foreach (var field in type.Fields)
        {
            if (!field.IsStatic || field.IsLiteral)
                continue;

            if (field.CustomAttributes.Any(IsConfigAttribute))
                continue;

            var propertyName = TryGetAutoPropertyName(field.Name);
            if (propertyName != null && type.Properties.Any(property => property.Name == propertyName && property.CustomAttributes.Any(IsConfigAttribute)))
                continue;

            if (!CanEmitLocalStaticField(field.FieldType))
                continue;

            defaults.TryGetValue($"field:{field.Name}", out var defaultValue);
            _localStaticFields.Add(
                new LocalStaticFieldEntry
                {
                    Name = field.Name,
                    CppIdentifier = BuildUniqueLocalStaticIdentifier(type.FullName, field.Name),
                    DeclaringTypeFullName = type.FullName,
                    Type = field.FieldType,
                    DefaultValueCpp = defaultValue?.Code,
                    StringArrayElements = defaultValue?.StringArrayElements,
                }
            );
        }
    }

    private void LoadCustomType(TypeDefinition type)
    {
        if (!type.CustomAttributes.Any(IsCustomTypeAttribute))
            return;

        if (type.BaseType == null)
            throw new InvalidOperationException($"[CustomType] requires a base type: {type.FullName}");

        var defaults = ReadInstanceFieldDefaults(type);
        var fields = new List<CustomTypeFieldEntry>();
        foreach (var field in type.Fields)
        {
            if (field.IsStatic || field.IsLiteral || field.Name.StartsWith("<", StringComparison.Ordinal))
                continue;

            if (!field.IsPublic && !field.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "SerializeField"))
                continue;

            fields.Add(
                new CustomTypeFieldEntry
                {
                    Name = field.Name,
                    CppType = _typeSystem.MapType(field.FieldType),
                    DefaultValueCpp = defaults.TryGetValue(field.Name, out var defaultValue) ? defaultValue : null,
                }
            );
        }

        var methods = new List<CustomTypeMethodEntry>();
        foreach (var method in type.Methods)
        {
            if (method.IsStatic || !method.HasBody || method.IsConstructor || method.IsGetter || method.IsSetter)
                continue;

            methods.Add(
                new CustomTypeMethodEntry
                {
                    Method = method,
                    CppName = CppIdentifier.Sanitize(method.Name),
                }
            );
        }

        _customTypes.Add(
            new CustomTypeEntry
            {
                Type = type,
                CppNamespace = _typeSystem.MapNamespace(type.Namespace),
                CppName = _typeSystem.ComposeTypeName(type),
                BaseCppType = _typeSystem.MapType(type.BaseType).TrimEnd('*'),
                DllName = _module?.Assembly.Name.Name ?? type.Module.Assembly.Name.Name,
                Fields = fields,
                Methods = methods,
            }
        );
    }

    private Dictionary<string, string> ReadInstanceFieldDefaults(TypeDefinition type)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var ctor = type.Methods.FirstOrDefault(method => method.IsConstructor && !method.IsStatic && method.HasBody && method.Parameters.Count == 0);
        if (ctor?.Body == null)
            return result;

        var stack = new Stack<CppExpression>();
        foreach (var instruction in ctor.Body.Instructions)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Ldarg_0:
                    stack.Push(new CppExpression { Code = "this" });
                    break;
                case Code.Ldc_I4_0:
                case Code.Ldc_I4_1:
                case Code.Ldc_I4_2:
                case Code.Ldc_I4_3:
                case Code.Ldc_I4_4:
                case Code.Ldc_I4_5:
                case Code.Ldc_I4_6:
                case Code.Ldc_I4_7:
                case Code.Ldc_I4_8:
                    stack.Push(new CppExpression { Code = ((int)instruction.OpCode.Code - (int)Code.Ldc_I4_0).ToString(CultureInfo.InvariantCulture) });
                    break;
                case Code.Ldc_I4:
                case Code.Ldc_I4_S:
                    stack.Push(new CppExpression { Code = Convert.ToInt32(instruction.Operand, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) });
                    break;
                case Code.Ldstr:
                    stack.Push(new CppExpression { Code = CppLiteral.String((string)instruction.Operand) });
                    break;
                case Code.Stfld:
                {
                    if (stack.Count < 2)
                    {
                        stack.Clear();
                        break;
                    }

                    var value = stack.Pop();
                    var target = stack.Pop();
                    var field = (FieldReference)instruction.Operand;
                    if (target.Code == "this" && field.DeclaringType.FullName == type.FullName)
                        result[field.Name] = NormalizeDefaultValue(field.FieldType, value.Code);
                    break;
                }
                case Code.Call:
                case Code.Callvirt:
                case Code.Ret:
                case Code.Nop:
                    stack.Clear();
                    break;
                default:
                    stack.Clear();
                    break;
            }
        }

        return result;
    }

    private void LoadBsmlRegistrations(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            foreach (var attribute in method.CustomAttributes.Where(IsMenuButtonAttribute))
            {
                ValidateMenuButtonMethod(method);
                _menuButtons.Add(new MenuButtonRegistration(method, attribute.ConstructorArguments.Count > 0 ? attribute.ConstructorArguments[0].Value?.ToString() ?? "" : "", attribute.ConstructorArguments.Count > 1 ? attribute.ConstructorArguments[1].Value?.ToString() ?? "" : ""));
                AddHelperMethod(method);
            }

            foreach (var attribute in method.CustomAttributes.Where(IsGameplaySetupTabAttribute))
            {
                ValidateGameplaySetupTabMethod(method);
                _gameplaySetupTabs.Add(new GameplaySetupTabRegistration(method, attribute.ConstructorArguments.Count > 0 ? attribute.ConstructorArguments[0].Value?.ToString() ?? "" : "", ReadNamedAttributeInt32(attribute, "MenuType", 15)));
                AddHelperMethod(method);
            }
        }
    }

    private void LoadHelperMethods(TypeDefinition type)
    {
        if (!type.Methods.Any(method => method.CustomAttributes.Any(IsHookAttribute) || method.CustomAttributes.Any(IsMenuButtonAttribute) || method.CustomAttributes.Any(IsGameplaySetupTabAttribute)))
            return;

        foreach (var method in type.Methods)
        {
            if (!method.IsStatic || !method.HasBody || method.IsConstructor || method.IsGetter || method.IsSetter)
                continue;

            if (method.CustomAttributes.Any(IsHookAttribute))
                continue;

            AddHelperMethod(method);
        }
    }

    private void AddHelperMethod(MethodDefinition method)
    {
        if (_helperMethods.Contains(method))
            return;

        _helperMethods.Add(method);
    }

    private static void ValidateMenuButtonMethod(MethodDefinition method)
    {
        if (!method.IsStatic || method.ReturnType.FullName != "System.Void" || method.Parameters.Count != 0)
            throw new InvalidOperationException($"[MenuButton] methods must be static void with no parameters: {method.FullName}");
    }

    private static void ValidateGameplaySetupTabMethod(MethodDefinition method)
    {
        if (!method.IsStatic || method.ReturnType.FullName != "System.Void")
            throw new InvalidOperationException($"[GameplaySetupTab] methods must be static void: {method.FullName}");

        if (method.Parameters.Count != 2 || method.Parameters[0].ParameterType.FullName != "UnityEngine.GameObject" || method.Parameters[1].ParameterType.FullName != "System.Boolean")
        {
            throw new InvalidOperationException($"[GameplaySetupTab] methods must have signature static void Method(UnityEngine.GameObject, bool): {method.FullName}");
        }
    }

    private static string? ReadNamedAttributeDefaultValue(CustomAttribute attribute, TypeReference targetType)
    {
        foreach (var property in attribute.Properties)
        {
            if (string.Equals(property.Name, "DefaultValue", StringComparison.Ordinal))
                return ConvertAttributeValueToCppLiteral(property.Argument.Value, targetType);
        }

        foreach (var field in attribute.Fields)
        {
            if (string.Equals(field.Name, "DefaultValue", StringComparison.Ordinal))
                return ConvertAttributeValueToCppLiteral(field.Argument.Value, targetType);
        }

        return null;
    }

    private static string? ConvertAttributeValueToCppLiteral(object? value, TypeReference targetType)
    {
        if (value == null)
            return null;

        if (value is CustomAttributeArgument nestedArgument)
            value = nestedArgument.Value;

        return targetType.FullName switch
        {
            "System.Boolean" => value is bool boolValue ? (boolValue ? "true" : "false") : null,
            "System.Single" => value is float floatValue ? floatValue.ToString("R", CultureInfo.InvariantCulture) : Convert.ToSingle(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
            "System.Double" => value is double doubleValue ? doubleValue.ToString("R", CultureInfo.InvariantCulture) : Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
            "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" => Convert.ToString(value, CultureInfo.InvariantCulture),
            "System.String" => value is string text ? CppLiteral.String(text) : CppLiteral.String(value.ToString() ?? string.Empty),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }

    private static bool ReadNamedAttributeBoolean(CustomAttribute attribute, string name)
    {
        foreach (var property in attribute.Properties)
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal) && property.Argument.Value is bool value)
                return value;
        }

        foreach (var field in attribute.Fields)
        {
            if (string.Equals(field.Name, name, StringComparison.Ordinal) && field.Argument.Value is bool value)
                return value;
        }

        return false;
    }

    private static int ReadNamedAttributeInt32(CustomAttribute attribute, string name, int defaultValue)
    {
        foreach (var property in attribute.Properties)
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
                return ConvertAttributeValueToInt32(property.Argument.Value, defaultValue);
        }

        foreach (var field in attribute.Fields)
        {
            if (string.Equals(field.Name, name, StringComparison.Ordinal))
                return ConvertAttributeValueToInt32(field.Argument.Value, defaultValue);
        }

        return defaultValue;
    }

    private static int ConvertAttributeValueToInt32(object? value, int defaultValue)
    {
        if (value is CustomAttributeArgument nestedArgument)
            value = nestedArgument.Value;

        return value switch
        {
            int intValue => intValue,
            byte byteValue => byteValue,
            sbyte sbyteValue => sbyteValue,
            short shortValue => shortValue,
            ushort ushortValue => ushortValue,
            _ => defaultValue,
        };
    }

    private static string? ReadHookMethodName(CustomAttribute attribute)
    {
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Type.FullName == "System.Type")
                continue;

            if (argument.Value is string methodName)
                return methodName;
        }

        return ReadNamedAttributeString(attribute, "MethodName");
    }

    private static HookPhase ResolveHookPhase(CustomAttribute attribute, MethodDefinition method)
    {
        var explicitPhase = ReadNamedAttributeHookPhase(attribute, "Phase");
        if (explicitPhase.HasValue)
            return explicitPhase.Value;

        if (method.Name.EndsWith("Prefix", StringComparison.Ordinal))
            return HookPhase.Prefix;
        if (method.Name.EndsWith("Postfix", StringComparison.Ordinal))
            return HookPhase.Postfix;

        return HookPhase.Full;
    }

    private static HookPhase? ReadNamedAttributeHookPhase(CustomAttribute attribute, string name)
    {
        foreach (var property in attribute.Properties)
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
                return ConvertHookPhaseValue(property.Argument.Value);
        }

        foreach (var field in attribute.Fields)
        {
            if (string.Equals(field.Name, name, StringComparison.Ordinal))
                return ConvertHookPhaseValue(field.Argument.Value);
        }

        return null;
    }

    private static HookPhase? ConvertHookPhaseValue(object? value)
    {
        return value switch
        {
            HookPhase phase => phase,
            int intValue => (HookPhase)intValue,
            byte byteValue => (HookPhase)byteValue,
            string text when Enum.TryParse(text, true, out HookPhase parsed) => parsed,
            _ => null,
        };
    }

    private TypeReference? ReadHookTargetType(CustomAttribute attribute)
    {
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Type.FullName == "System.Type")
                return NormalizeHookTargetType(argument.Value);
        }

        foreach (var property in attribute.Properties)
        {
            if (string.Equals(property.Name, "TargetType", StringComparison.Ordinal))
                return NormalizeHookTargetType(property.Argument.Value);
        }

        foreach (var field in attribute.Fields)
        {
            if (string.Equals(field.Name, "TargetType", StringComparison.Ordinal))
                return NormalizeHookTargetType(field.Argument.Value);
        }

        return null;
    }

    private TypeReference? NormalizeHookTargetType(object? value)
    {
        return value switch
        {
            TypeReference typeReference => _module?.ImportReference(typeReference) ?? typeReference,
            _ => null,
        };
    }

    private static string MapTargetMethodName(HookDefinition hook)
    {
        return hook.IsConstructor ? "_ctor" : hook.TargetMethod;
    }

    private static string BuildHookName(TypeReference targetType, string targetMethod, MethodDefinition hookMethod)
    {
        var typeToken = CppIdentifier.Sanitize(targetType.FullName, "Type");
        var methodToken = CppIdentifier.Sanitize(targetMethod, "Method");
        var signatureToken = BuildSignatureToken(hookMethod.Parameters.Select(parameter => parameter.ParameterType));

        return signatureToken.Length == 0 ? $"{typeToken}_{methodToken}_Hook" : $"{typeToken}_{methodToken}_{signatureToken}_Hook";
    }

    private static string BuildSignatureToken(IEnumerable<TypeReference> parameters)
    {
        var tokens = parameters.Select(parameter => CppIdentifier.Sanitize(parameter.FullName, "Arg")).Where(token => !string.IsNullOrWhiteSpace(token)).ToArray();

        return tokens.Length == 0 ? "" : string.Join("_", tokens);
    }

    private static string GetHelperFunctionName(HookDefinition hook)
    {
        return CppIdentifier.Sanitize(hook.Method.Name, "HookHelper");
    }

    private static string GetHelperFunctionName(MethodDefinition method)
    {
        var typeToken = CppIdentifier.Sanitize(method.DeclaringType.FullName, "Type");
        var methodToken = CppIdentifier.Sanitize(method.Name, "Helper");
        var signatureToken = BuildSignatureToken(method.Parameters.Select(parameter => parameter.ParameterType));
        return signatureToken.Length == 0 ? $"{typeToken}_{methodToken}" : $"{typeToken}_{methodToken}_{signatureToken}";
    }

    private List<HookEmission> BuildHookEmissions(Dictionary<HookDefinition, IlMethodTranslator> bodyGenerators)
    {
        var orderedHooks = _hooks.Select((hook, index) => (hook, index)).ToList();
        var grouped = orderedHooks.GroupBy(item => BuildHookGroupKey(item.hook)).OrderBy(group => group.Min(item => item.index));

        var emissions = new List<HookEmission>();

        foreach (var group in grouped)
        {
            var hooks = group.Select(item => item.hook).ToList();
            var fullHooks = hooks.Where(hook => hook.Phase == HookPhase.Full).ToList();
            var prefixHooks = hooks.Where(hook => hook.Phase == HookPhase.Prefix).ToList();
            var postfixHooks = hooks.Where(hook => hook.Phase == HookPhase.Postfix).ToList();

            if (fullHooks.Count > 0)
            {
                if (prefixHooks.Count > 0 || postfixHooks.Count > 0)
                    throw new InvalidOperationException($"Hook group for {hooks[0].TargetType.FullName}.{hooks[0].TargetMethod} mixes full hooks with prefix/postfix hooks.");

                foreach (var hook in fullHooks)
                    emissions.Add(new HookEmission(hook, null, null, bodyGenerators[hook], null, null));

                continue;
            }

            if (prefixHooks.Count > 1)
                throw new InvalidOperationException($"Hook group for {hooks[0].TargetType.FullName}.{hooks[0].TargetMethod} has multiple prefix hooks.");
            if (postfixHooks.Count > 1)
                throw new InvalidOperationException($"Hook group for {hooks[0].TargetType.FullName}.{hooks[0].TargetMethod} has multiple postfix hooks.");

            var prefixHook = prefixHooks.SingleOrDefault();
            var postfixHook = postfixHooks.SingleOrDefault();
            if (prefixHook == null && postfixHook == null)
                continue;

            if (prefixHook != null && postfixHook != null)
            {
                EnsureHookSignatureMatch(prefixHook, postfixHook);
                if (!string.Equals(prefixHook.HookName, postfixHook.HookName, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Hook group for {hooks[0].TargetType.FullName}.{hooks[0].TargetMethod} has mismatched hook names ({prefixHook.HookName} vs {postfixHook.HookName}).");
            }

            var signatureHook = prefixHook ?? postfixHook!;
            emissions.Add(new HookEmission(signatureHook, prefixHook, postfixHook, null, prefixHook != null ? bodyGenerators[prefixHook] : null, postfixHook != null ? bodyGenerators[postfixHook] : null));
        }

        return emissions;
    }

    private List<HelperMethodEmission> BuildHelperMethodEmissions(IReadOnlyDictionary<string, string> localMethodNames)
    {
        var result = new List<HelperMethodEmission>();
        foreach (var method in _helperMethods)
        {
            var functionName = localMethodNames[method.FullName];
            var syntheticHook = new HookDefinition
            {
                HookName = functionName,
                TargetMethod = method.Name,
                TargetType = method.DeclaringType,
                Method = method,
                IsConstructor = false,
                Phase = HookPhase.Full,
            };

            var translator = BuildStructuredTranslator(syntheticHook, localMethodNames);
            result.Add(new HelperMethodEmission(method, functionName, translator));
        }

        return result;
    }

    private IlMethodTranslator BuildStructuredTranslator(HookDefinition hook, IReadOnlyDictionary<string, string> localMethodNames)
    {
        var translator = new IlMethodTranslator(hook, _typeSystem, _configValues, _localStaticFields, _metadataIndex, localMethodNames);
        translator.Translate();
        return translator;
    }

    private static string NormalizeGeneratedSource(string source)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var marker = " = ::il2cpp_utils::NewSpecific<";
            var markerIndex = lines[i].IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
                continue;

            var lhs = lines[i][..markerIndex].Trim();
            var callStart = lines[i].LastIndexOf('(');
            var callEnd = lines[i].LastIndexOf(");", StringComparison.Ordinal);
            if (callStart < 0 || callEnd < callStart)
                continue;

            var args = lines[i][(callStart + 1)..callEnd].Trim();
            if (string.Equals(args, lhs, StringComparison.Ordinal))
                args = string.Empty;
            var indentLength = lines[i].Length - lines[i].TrimStart().Length;
            var indent = lines[i][..indentLength];
            lines[i] = $"{indent}{lhs} = ::il2cpp_utils::NewSpecific<decltype({lhs})>({args});";
        }

        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = Regex.Replace(lines[i], @"\(\(([^()]+)\)\)", "($1)");
            lines[i] = Regex.Replace(lines[i], @"if \(\(([^()]+)\)\)", "if ($1)");
        }

        var sourceText = string.Join("\n", lines);
        sourceText = SimplifyFunctionLocalTemps(sourceText);

        sourceText = Regex.Replace(sourceText, @"(\r?\n){3,}", Environment.NewLine + Environment.NewLine);
        return sourceText.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    }

    private static string SimplifyFunctionLocalTemps(string sourceText)
    {
        var lines = sourceText.Split('\n').ToList();
        var functionStartRegex = new Regex(@"\)\s*\{\s*$", RegexOptions.Compiled);

        for (var i = 0; i < lines.Count; i++)
        {
            if (!functionStartRegex.IsMatch(lines[i]))
                continue;

            var start = i;
            var depth = CountBraces(lines[i]);
            var end = i;
            while (depth > 0 && end + 1 < lines.Count)
            {
                end++;
                depth += CountBraces(lines[end]);
            }

            var block = string.Join("\n", lines.Skip(start).Take(end - start + 1));
            var simplified = SimplifySingleFunctionBlock(block).Split('\n');
            lines.RemoveRange(start, end - start + 1);
            lines.InsertRange(start, simplified);
            i = start + simplified.Length - 1;
        }

        return string.Join("\n", lines);
    }

    private static string SimplifySingleFunctionBlock(string block)
    {
        var inlineIfRegex = new Regex(@"^(?<assignIndent>\s*)(?<name>local\d+(?:_\d+)?) = (?<expr>.+);\r?\n(?<ifIndent>\s*)if \((?:\()?(?<condition>\k<name>)(?:\))?\)", RegexOptions.Multiline);
        block = inlineIfRegex.Replace(
            block,
            match =>
            {
                var localName = match.Groups["name"].Value;
                var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
                return usageCount == 3 ? $"{match.Groups["ifIndent"].Value}if ({match.Groups["expr"].Value})" : match.Value;
            }
        );

        var duplicateExprIfRegex = new Regex(@"^(?<assignIndent>\s*)(?<name>local\d+(?:_\d+)?) = (?<expr>.+);\r?\n(?<ifIndent>\s*)if \((?:\()?(?<condition>\k<expr>)(?:\))?\)", RegexOptions.Multiline);
        block = duplicateExprIfRegex.Replace(
            block,
            match =>
            {
                var localName = match.Groups["name"].Value;
                var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
                return usageCount == 2 ? $"{match.Groups["ifIndent"].Value}if ({match.Groups["expr"].Value})" : match.Value;
            }
        );

        var wrappedDuplicateExprIfRegex = new Regex(@"^(?<assignIndent>\s*)(?<name>local\d+(?:_\d+)?) = \((?<expr>.+)\);\r?\n(?<ifIndent>\s*)if \((?:\()?(?<condition>\k<expr>)(?:\))?\)", RegexOptions.Multiline);
        block = wrappedDuplicateExprIfRegex.Replace(
            block,
            match =>
            {
                var localName = match.Groups["name"].Value;
                var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
                return usageCount == 2 ? $"{match.Groups["ifIndent"].Value}if ({match.Groups["expr"].Value})" : match.Value;
            }
        );

        var inlineReturnRegex = new Regex(@"^(?<assignIndent>\s*)(?<name>local\d+(?:_\d+)?) = (?<expr>.+);\r?\n(?<returnIndent>\s*)return (?<returnName>\k<name>);", RegexOptions.Multiline);
        block = inlineReturnRegex.Replace(
            block,
            match =>
            {
                var localName = match.Groups["name"].Value;
                var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
                return usageCount == 3 ? $"{match.Groups["returnIndent"].Value}return {match.Groups["expr"].Value};" : match.Value;
            }
        );

        var localAssignmentRegex = new Regex(@"^(?<indent>\s*)(?<name>local\d+(?:_\d+)?) = (?<expr>.+);\s*$", RegexOptions.Multiline);
        var changed = true;
        while (changed)
        {
            changed = false;
            block = localAssignmentRegex.Replace(
                block,
                match =>
                {
                    var localName = match.Groups["name"].Value;
                    var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
                    if (usageCount == 2)
                    {
                        changed = true;
                        return string.Empty;
                    }

                    return match.Value;
                }
            );
        }

        var localDeclarationRegex = new Regex(@"^(?<indent>\s*)(?<type>[\w:<>]+(?:\s*[*&])?)\s+(?<name>local\d+(?:_\d+)?)\{\};\s*$", RegexOptions.Multiline);
        block = localDeclarationRegex.Replace(
            block,
            match =>
            {
                var localName = match.Groups["name"].Value;
                var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
                return usageCount <= 1 ? string.Empty : match.Value;
            }
        );

        return block;
    }

    private static int CountBraces(string line)
    {
        var depth = 0;
        foreach (var ch in line)
        {
            if (ch == '{')
                depth++;
            else if (ch == '}')
                depth--;
        }

        return depth;
    }

    private static string BuildHookGroupKey(HookDefinition hook)
    {
        var parameterKey = string.Join("|", hook.Method.Parameters.Select(parameter => parameter.ParameterType.FullName));
        return $"{hook.TargetType.FullName}|{hook.TargetMethod}|{hook.IsConstructor}|{parameterKey}";
    }

    private static void EnsureHookSignatureMatch(HookDefinition left, HookDefinition right)
    {
        if (!string.Equals(left.Method.ReturnType.FullName, right.Method.ReturnType.FullName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Hook methods {left.Method.FullName} and {right.Method.FullName} must return the same type.");

        if (left.Method.Parameters.Count != right.Method.Parameters.Count)
            throw new InvalidOperationException($"Hook methods {left.Method.FullName} and {right.Method.FullName} must have the same parameter count.");

        for (var i = 0; i < left.Method.Parameters.Count; i++)
        {
            var leftParam = left.Method.Parameters[i];
            var rightParam = right.Method.Parameters[i];
            if (!string.Equals(leftParam.ParameterType.FullName, rightParam.ParameterType.FullName, StringComparison.Ordinal))
                throw new InvalidOperationException($"Hook methods {left.Method.FullName} and {right.Method.FullName} must have matching parameter types.");
        }
    }

    private void AssignConfigIdentifiers()
    {
        foreach (var configGroup in _configValues.GroupBy(config => config.Name, StringComparer.Ordinal))
        {
            if (configGroup.Count() == 1)
                continue;

            foreach (var config in configGroup)
                config.CppIdentifier = BuildUniqueConfigIdentifier(config);
        }
    }

    private static string BuildUniqueConfigIdentifier(ConfigEntry config)
    {
        var typeToken = CppIdentifier.Sanitize(config.DeclaringTypeFullName, "Config");
        var nameToken = CppIdentifier.Sanitize(config.Name, "Value");
        return $"{typeToken}_{nameToken}";
    }

    private static string BuildUniqueLocalStaticIdentifier(string declaringTypeFullName, string fieldName)
    {
        var typeToken = CppIdentifier.Sanitize(declaringTypeFullName, "Static");
        var nameToken = CppIdentifier.Sanitize(fieldName, "Value");
        return $"{typeToken}_{nameToken}";
    }

    private static bool IsTranspilerRelevantType(TypeDefinition type)
    {
        if (type.CustomAttributes.Any(IsCustomTypeAttribute))
            return true;

        if (type.CustomAttributes.Any(IsModAttribute))
            return true;

        if (type.Methods.Any(method => method.CustomAttributes.Any(IsHookAttribute)))
            return true;

        if (type.Fields.Any(field => field.CustomAttributes.Any(IsConfigAttribute)))
            return true;

        return type.Properties.Any(property => property.CustomAttributes.Any(IsConfigAttribute));
    }

    private bool CanEmitLocalStaticField(TypeReference type)
    {
        if (type is ByReferenceType byReferenceType)
            return CanEmitLocalStaticField(byReferenceType.ElementType);

        if (type is ArrayType arrayType)
            return CanEmitLocalStaticField(arrayType.ElementType);

        if (type is GenericInstanceType genericInstanceType)
        {
            if (!CanEmitLocalStaticField(genericInstanceType.ElementType))
                return false;

            foreach (var argument in genericInstanceType.GenericArguments)
            {
                if (!CanEmitLocalStaticField(argument))
                    return false;
            }

            return true;
        }

        return true;
    }

    private static string? TryGetAutoPropertyName(string fieldName)
    {
        const string suffix = ">k__BackingField";
        if (!fieldName.StartsWith("<", StringComparison.Ordinal) || !fieldName.EndsWith(suffix, StringComparison.Ordinal))
            return null;

        return fieldName[1..^suffix.Length];
    }

    private IEnumerable<string> CollectBodyIncludes(MethodDefinition method)
    {
        if (!method.HasBody || method.Body == null)
            yield break;

        foreach (var variable in method.Body.Variables)
        {
            if (_typeSystem.GetIncludePath(variable.VariableType) is { } variableInclude)
                yield return variableInclude;
        }

        foreach (var instruction in method.Body.Instructions)
        {
            switch (instruction.Operand)
            {
                case MethodReference referencedMethod:
                    if (IsTranspilerOwnedType(referencedMethod.DeclaringType))
                        break;

                    if (_typeSystem.GetIncludePath(referencedMethod.DeclaringType) is { } methodDeclaringTypeInclude)
                        yield return methodDeclaringTypeInclude;

                    if (_typeSystem.GetIncludePath(referencedMethod.ReturnType) is { } methodReturnTypeInclude)
                        yield return methodReturnTypeInclude;

                    foreach (var parameter in referencedMethod.Parameters)
                    {
                        if (_typeSystem.GetIncludePath(parameter.ParameterType) is { } methodParameterInclude)
                            yield return methodParameterInclude;
                    }

                    break;

                case FieldReference referencedField:
                    if (IsTranspilerOwnedType(referencedField.DeclaringType))
                        break;

                    if (_typeSystem.GetIncludePath(referencedField.DeclaringType) is { } fieldDeclaringTypeInclude)
                        yield return fieldDeclaringTypeInclude;

                    if (_typeSystem.GetIncludePath(referencedField.FieldType) is { } fieldTypeInclude)
                        yield return fieldTypeInclude;

                    break;
            }
        }
    }

    private IEnumerable<string> CollectCurrentModuleIncludePaths()
    {
        if (_module == null)
            yield break;

        var pending = new Stack<TypeDefinition>(_module.Types.Where(IsTranspilerRelevantType).Reverse());
        while (pending.Count > 0)
        {
            var type = pending.Pop();
            if (_typeSystem.GetIncludePath(type) is { } include)
                yield return include;

            for (var i = type.NestedTypes.Count - 1; i >= 0; i--)
                pending.Push(type.NestedTypes[i]);
        }
    }

    private bool IsTranspilerOwnedType(TypeReference? type)
    {
        var resolved = type?.Resolve();
        return resolved != null && IsTranspilerRelevantType(resolved);
    }
}
