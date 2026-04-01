using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed class Transpiler
{
    private sealed record HookEmission(HookDefinition Hook, HookDefinition? PrefixHook, HookDefinition? PostfixHook, IlMethodTranslator? FullBody, IlMethodTranslator? PrefixBody, IlMethodTranslator? PostfixBody);

    private readonly string _assemblyPath;
    private readonly List<HookDefinition> _hooks = new();
    private readonly List<ConfigEntry> _configValues = new();
    private readonly CppTypeSystem _typeSystem = new();
    private readonly TypeMetadataIndex _metadataIndex;
    private ModuleDefinition? _module;
    private ModMetadata _modMetadata = new();

    public Transpiler(string assemblyPath)
    {
        _assemblyPath = assemblyPath;
        _metadataIndex = TypeMetadataIndex.Load(assemblyPath);
    }

    public List<GeneratedArtifact> GeneratedArtifacts { get; } = new();

    public void Load()
    {
        var resolver = new DefaultAssemblyResolver();
        var assemblyDirectory = Path.GetDirectoryName(_assemblyPath) ?? Directory.GetCurrentDirectory();
        resolver.AddSearchDirectory(assemblyDirectory);

        var readerParameters = new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadSymbols = File.Exists(Path.ChangeExtension(_assemblyPath, ".pdb")),
            InMemory = true,
        };

        _module = ModuleDefinition.ReadModule(_assemblyPath, readerParameters);

        foreach (var type in _module.Types)
            ProcessType(type);

        AssignConfigIdentifiers();
    }

    public void GenerateOutput(string outputDirectory)
    {
        if (_module == null)
            throw new InvalidOperationException("Load must be called before GenerateOutput.");

        Directory.CreateDirectory(outputDirectory);

        GenerateConfigHeader(outputDirectory);
        GenerateMainHeader(outputDirectory);
        GenerateMainSource(outputDirectory);
    }

    private void ProcessType(TypeDefinition type)
    {
        LoadModMetadata(type);
        LoadConfigs(type);
        LoadHooks(type);

        foreach (var nestedType in type.NestedTypes)
            ProcessType(nestedType);
    }

    private void LoadModMetadata(TypeDefinition type)
    {
        var modAttribute = type.CustomAttributes.FirstOrDefault(IsModAttribute);
        if (modAttribute == null)
            return;

        if (modAttribute.ConstructorArguments.Count >= 2)
        {
            _modMetadata.Id = modAttribute.ConstructorArguments[0].Value?.ToString() ?? _modMetadata.Id;
            _modMetadata.Version = modAttribute.ConstructorArguments[1].Value?.ToString() ?? _modMetadata.Version;
        }
    }

    private void LoadConfigs(TypeDefinition type)
    {
        var defaults = ReadStaticDefaults(type);

        foreach (var property in type.Properties)
        {
            var configAttribute = property.CustomAttributes.FirstOrDefault(IsConfigAttribute);
            if (configAttribute == null)
                continue;

            defaults.TryGetValue($"property:{property.Name}", out var defaultValueCpp);

            _configValues.Add(
                new ConfigEntry
                {
                    Name = property.Name,
                    CppIdentifier = CppIdentifier.Sanitize(property.Name),
                    DeclaringTypeFullName = type.FullName,
                    Type = property.PropertyType,
                    Description = ReadNamedAttributeString(configAttribute, "Description") ?? "",
                    DefaultValueCpp = defaultValueCpp,
                }
            );
        }

        foreach (var field in type.Fields)
        {
            var configAttribute = field.CustomAttributes.FirstOrDefault(IsConfigAttribute);
            if (configAttribute == null)
                continue;

            defaults.TryGetValue($"field:{field.Name}", out var defaultValueCpp);

            _configValues.Add(
                new ConfigEntry
                {
                    Name = field.Name,
                    CppIdentifier = CppIdentifier.Sanitize(field.Name),
                    DeclaringTypeFullName = type.FullName,
                    Type = field.FieldType,
                    Description = ReadNamedAttributeString(configAttribute, "Description") ?? "",
                    DefaultValueCpp = defaultValueCpp,
                }
            );
        }
    }

    private void LoadHooks(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            var hookAttribute = method.CustomAttributes.FirstOrDefault(IsHookAttribute);
            if (hookAttribute == null)
                continue;

            if (!method.IsStatic)
                throw new InvalidOperationException($"Hook methods must be static: {method.FullName}");

            if (method.Parameters.Count == 0)
                throw new InvalidOperationException($"Hook methods must have at least the self parameter: {method.FullName}");

            var targetType = method.Parameters[0].ParameterType;
            var targetMethod = ReadHookMethodName(hookAttribute) ?? method.Name;
            var isConstructor = ReadNamedAttributeBoolean(hookAttribute, "IsConstructor") || string.Equals(targetMethod, ".ctor", StringComparison.Ordinal);
            if (isConstructor)
                targetMethod = ".ctor";

            if (ReadHookTargetType(hookAttribute) is { } explicitTargetType)
                targetType = explicitTargetType;

            var explicitClassName = ReadNamedAttributeString(hookAttribute, "ClassName");
            if (!string.IsNullOrWhiteSpace(explicitClassName) && ReadHookTargetType(hookAttribute) == null && !string.Equals(targetType.Name, explicitClassName, StringComparison.Ordinal))
            {
                targetType = TryResolveTypeByName(explicitClassName!, targetType);
            }

            var phase = ResolveHookPhase(hookAttribute, method);
            var hookName = BuildHookName(targetType, targetMethod, method);

            _hooks.Add(
                new HookDefinition
                {
                    HookName = hookName,
                    TargetMethod = targetMethod,
                    TargetType = targetType,
                    Method = method,
                    IsConstructor = isConstructor,
                    Phase = phase,
                }
            );
        }
    }

    private TypeReference TryResolveTypeByName(string className, TypeReference fallbackType)
    {
        if (_module == null)
            return fallbackType;

        var direct = _module.GetType(className) ?? _module.GetType($"{fallbackType.Namespace}.{className}");
        return direct ?? fallbackType;
    }

    private Dictionary<string, string> ReadStaticDefaults(TypeDefinition type)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var staticCtor = type.Methods.FirstOrDefault(method => method.IsConstructor && method.IsStatic && method.HasBody);
        if (staticCtor?.Body == null)
            return result;

        var stack = new Stack<CppExpression>();
        foreach (var instruction in staticCtor.Body.Instructions)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Ldc_I4_M1:
                    stack.Push(new CppExpression { Code = "-1" });
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
                case Code.Stsfld:
                {
                    var value = stack.Pop().Code;
                    var field = (FieldReference)instruction.Operand;
                    var propertyName = TryGetAutoPropertyName(field.Name);
                    result[propertyName != null ? $"property:{propertyName}" : $"field:{field.Name}"] = NormalizeDefaultValue(field.FieldType, value);
                    break;
                }
                case Code.Ret:
                case Code.Nop:
                    break;
                default:
                    stack.Clear();
                    break;
            }
        }

        return result;
    }

    private string NormalizeDefaultValue(TypeReference type, string value)
    {
        return type.FullName == "System.Boolean" ? (value == "0" ? "false" : "true") : value;
    }

    private void GenerateConfigHeader(string outputDirectory)
    {
        var writer = new CppCodeWriter();
        writer.WriteLine("#pragma once");
        writer.WriteLine();
        writer.WriteLine("#define MOD_EXPORT __attribute__((visibility(\"default\")))");
        writer.WriteLine("#define MOD_EXTERN_FUNC extern \"C\" MOD_EXPORT");
        writer.WriteLine();
        writer.WriteLine("#include \"beatsaber-hook/shared/utils/il2cpp-utils.hpp\"");
        writer.WriteLine();

        foreach (var config in _configValues)
        {
            var cppType = _typeSystem.MapType(config.Type);
            var defaultValue = config.DefaultValueCpp ?? _typeSystem.GetDefaultValue(config.Type);
            writer.WriteLine($"static {cppType} {config.CppIdentifier} = {defaultValue};");
        }

        WriteOutputFile(outputDirectory, Path.Combine("include", "_config.hpp"), writer.ToString());
    }

    private void GenerateMainHeader(string outputDirectory)
    {
        var writer = new CppCodeWriter();
        writer.WriteLine("#pragma once");
        writer.WriteLine();
        writer.WriteLine("#include \"scotland2/shared/modloader.h\"");
        writer.WriteLine("#include \"beatsaber-hook/shared/config/config-utils.hpp\"");
        writer.WriteLine("#include \"beatsaber-hook/shared/utils/hooking.hpp\"");
        writer.WriteLine("#include \"beatsaber-hook/shared/utils/il2cpp-functions.hpp\"");
        writer.WriteLine("#include \"beatsaber-hook/shared/utils/logging.hpp\"");
        writer.WriteLine("#include \"paper2_scotland2/shared/logger.hpp\"");
        writer.WriteLine("#include \"_config.hpp\"");
        writer.WriteLine();
        writer.WriteLine("Configuration &getConfig();");
        writer.WriteLine();
        writer.WriteLine($"constexpr auto PaperLogger = Paper::ConstLoggerContext(\"{_modMetadata.Id}\");");

        WriteOutputFile(outputDirectory, Path.Combine("include", "main.hpp"), writer.ToString());
    }

    private void GenerateMainSource(string outputDirectory)
    {
        var bodyGenerators = _hooks.ToDictionary(hook => hook, hook => new IlMethodTranslator(hook, _typeSystem, _configValues, _metadataIndex));

        foreach (var generator in bodyGenerators.Values)
            generator.Translate();

        var includeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hook in _hooks)
        {
            AddInclude(includeSet, _typeSystem.GetIncludePath(hook.TargetType));
            foreach (var parameter in hook.Method.Parameters)
                AddInclude(includeSet, _typeSystem.GetIncludePath(parameter.ParameterType));
        }

        foreach (var generator in bodyGenerators.Values)
        {
            foreach (var include in generator.RequiredIncludes)
                includeSet.Add(include);
        }

        var hookEmissions = BuildHookEmissions(bodyGenerators);

        var writer = new CppCodeWriter();
        writer.WriteLine("#include \"main.hpp\"");
        writer.WriteLine("#include \"scotland2/shared/modloader.h\"");
        writer.WriteLine();

        foreach (var include in includeSet.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            writer.WriteLine($"#include \"{include}\"");

        writer.WriteLine();
        writer.WriteLine($"static modloader::ModInfo modInfo{{\"{_modMetadata.Id}\", \"{_modMetadata.Version}\", 0}};");
        writer.WriteLine();
        writer.WriteLine("Configuration &getConfig() {");
        writer.WriteLine("    static Configuration config(modInfo);");
        writer.WriteLine("    return config;");
        writer.WriteLine("}");
        writer.WriteLine();

        foreach (var emission in hookEmissions)
        {
            if (emission.FullBody != null)
            {
                WriteHook(writer, emission.Hook, emission.FullBody);
                continue;
            }

            WriteHookWithPrefixPostfix(writer, emission.Hook, emission.PrefixHook, emission.PrefixBody, emission.PostfixHook, emission.PostfixBody);
        }

        writer.WriteLine("MOD_EXTERN_FUNC void late_load() noexcept {");
        writer.WriteLine("    il2cpp_functions::Init();");
        writer.WriteLine("    PaperLogger.info(\"Installing hooks...\");");
        writer.WriteLine();

        foreach (var emission in hookEmissions)
            writer.WriteLine($"    INSTALL_HOOK(PaperLogger, {emission.Hook.HookName});");

        writer.WriteLine();
        writer.WriteLine("    PaperLogger.info(\"Installed all hooks!\");");
        writer.WriteLine("}");

        WriteOutputFile(outputDirectory, Path.Combine("src", "main.cpp"), writer.ToString());
    }

    private void WriteHook(CppCodeWriter writer, HookDefinition hook, IlMethodTranslator bodyGenerator)
    {
        var returnType = _typeSystem.MapType(hook.Method.ReturnType);
        var parameters = hook.Method.Parameters.Select(parameter => $"{_typeSystem.MapType(parameter.ParameterType)} {CppIdentifier.Sanitize(parameter.Name)}").ToList();

        writer.WriteLine("MAKE_HOOK_MATCH(");
        writer.WriteLine($"    {hook.HookName},");
        writer.WriteLine($"    &{_typeSystem.MapNamespace(hook.TargetType.Namespace)}::{_typeSystem.ComposeTypeName(hook.TargetType)}::{MapTargetMethodName(hook)},");
        writer.WriteLine($"    {returnType},");
        writer.WriteLine($"    {string.Join(", ", parameters)}) {{");

        foreach (var line in bodyGenerator.Statements)
            writer.WriteLine($"    {line}");

        writer.WriteLine("}");
        writer.WriteLine();
    }

    private void WriteHookWithPrefixPostfix(CppCodeWriter writer, HookDefinition hook, HookDefinition? prefixHook, IlMethodTranslator? prefixBody, HookDefinition? postfixHook, IlMethodTranslator? postfixBody)
    {
        if (prefixHook == null && postfixHook == null)
            throw new InvalidOperationException($"Hook {hook.HookName} must have a prefix or postfix method.");

        var returnType = _typeSystem.MapType(hook.Method.ReturnType);
        var parameters = hook.Method.Parameters.Select(parameter => $"{_typeSystem.MapType(parameter.ParameterType)} {CppIdentifier.Sanitize(parameter.Name)}").ToList();
        var argumentList = string.Join(", ", hook.Method.Parameters.Select(parameter => CppIdentifier.Sanitize(parameter.Name)));

        if (prefixHook != null && prefixBody != null)
            WriteHelperFunction(writer, prefixHook, prefixBody);

        if (postfixHook != null && postfixBody != null)
            WriteHelperFunction(writer, postfixHook, postfixBody);

        writer.WriteLine("MAKE_HOOK_MATCH(");
        writer.WriteLine($"    {hook.HookName},");
        writer.WriteLine($"    &{_typeSystem.MapNamespace(hook.TargetType.Namespace)}::{_typeSystem.ComposeTypeName(hook.TargetType)}::{MapTargetMethodName(hook)},");
        writer.WriteLine($"    {returnType},");
        writer.WriteLine($"    {string.Join(", ", parameters)}) {{");

        if (prefixHook != null)
            writer.WriteLine($"    {GetHelperFunctionName(prefixHook)}({argumentList});");

        if (hook.Method.ReturnType.FullName == "System.Void")
        {
            writer.WriteLine($"    {hook.HookName}({argumentList});");
            if (postfixHook != null)
                writer.WriteLine($"    {GetHelperFunctionName(postfixHook)}({argumentList});");
            writer.WriteLine("    return;");
        }
        else
        {
            writer.WriteLine($"    auto result = {hook.HookName}({argumentList});");
            if (postfixHook != null)
                writer.WriteLine($"    {GetHelperFunctionName(postfixHook)}({argumentList});");
            writer.WriteLine("    return result;");
        }

        writer.WriteLine("}");
        writer.WriteLine();
    }

    private void WriteHelperFunction(CppCodeWriter writer, HookDefinition hook, IlMethodTranslator bodyGenerator)
    {
        var returnType = _typeSystem.MapType(hook.Method.ReturnType);
        var parameters = hook.Method.Parameters.Select(parameter => $"{_typeSystem.MapType(parameter.ParameterType)} {CppIdentifier.Sanitize(parameter.Name)}").ToList();

        writer.WriteLine($"static {returnType} {GetHelperFunctionName(hook)}({string.Join(", ", parameters)}) {{");
        foreach (var line in bodyGenerator.Statements)
            writer.WriteLine($"    {line}");
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private void WriteOutputFile(string outputDirectory, string relativePath, string content)
    {
        var fullPath = Path.Combine(outputDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        GeneratedArtifacts.Add(new GeneratedArtifact { Path = fullPath, Content = content });
    }

    private static void AddInclude(ISet<string> includes, string? include)
    {
        if (!string.IsNullOrWhiteSpace(include))
            includes.Add(include);
    }

    private static bool IsHookAttribute(CustomAttribute attribute) => attribute.AttributeType.Name is "HookAttribute" or "Hook";

    private static bool IsModAttribute(CustomAttribute attribute) => attribute.AttributeType.Name is "ModAttribute" or "Mod";

    private static bool IsConfigAttribute(CustomAttribute attribute) => attribute.AttributeType.Name is "ConfigAttribute" or "Config";

    private static string? ReadNamedAttributeString(CustomAttribute attribute, string name)
    {
        foreach (var property in attribute.Properties)
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
                return property.Argument.Value?.ToString();
        }

        foreach (var field in attribute.Fields)
        {
            if (string.Equals(field.Name, name, StringComparison.Ordinal))
                return field.Argument.Value?.ToString();
        }

        return null;
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

    private static string? TryGetAutoPropertyName(string fieldName)
    {
        const string suffix = ">k__BackingField";
        if (!fieldName.StartsWith("<", StringComparison.Ordinal) || !fieldName.EndsWith(suffix, StringComparison.Ordinal))
            return null;

        return fieldName[1..^suffix.Length];
    }
}
