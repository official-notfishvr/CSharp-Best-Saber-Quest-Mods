using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed class CecilTranspiler
{
    private readonly string _assemblyPath;
    private readonly List<HookInfo> _hooks = new();
    private readonly List<ConfigValueInfo> _configValues = new();
    private readonly CppTypeSystem _typeSystem = new();
    private readonly TypeMetadataIndex _metadataIndex;
    private ModuleDefinition? _module;
    private ModInfo _modInfo = new();

    public CecilTranspiler(string assemblyPath)
    {
        _assemblyPath = assemblyPath;
        _metadataIndex = TypeMetadataIndex.Load(assemblyPath);
    }

    public List<GeneratedFile> GeneratedFiles { get; } = new();

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
        LoadModInfo(type);
        LoadConfigs(type);
        LoadHooks(type);

        foreach (var nestedType in type.NestedTypes)
            ProcessType(nestedType);
    }

    private void LoadModInfo(TypeDefinition type)
    {
        var modAttribute = type.CustomAttributes.FirstOrDefault(IsModAttribute);
        if (modAttribute == null)
            return;

        if (modAttribute.ConstructorArguments.Count >= 2)
        {
            _modInfo.Id = modAttribute.ConstructorArguments[0].Value?.ToString() ?? _modInfo.Id;
            _modInfo.Version = modAttribute.ConstructorArguments[1].Value?.ToString() ?? _modInfo.Version;
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
                new ConfigValueInfo
                {
                    Name = property.Name,
                    CppIdentifier = property.Name,
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
                new ConfigValueInfo
                {
                    Name = field.Name,
                    CppIdentifier = field.Name,
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

            _hooks.Add(
                new HookInfo
                {
                    HookName = method.Name + "Hook",
                    TargetMethod = targetMethod,
                    TargetType = targetType,
                    Method = method,
                    IsConstructor = isConstructor,
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

        var stack = new Stack<CppValue>();
        foreach (var instruction in staticCtor.Body.Instructions)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Ldc_I4_M1:
                    stack.Push(new CppValue { Code = "-1" });
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
                    stack.Push(new CppValue { Code = ((int)instruction.OpCode.Code - (int)Code.Ldc_I4_0).ToString(CultureInfo.InvariantCulture) });
                    break;
                case Code.Ldc_I4:
                case Code.Ldc_I4_S:
                    stack.Push(new CppValue { Code = Convert.ToInt32(instruction.Operand, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) });
                    break;
                case Code.Ldstr:
                    stack.Push(new CppValue { Code = CppLiteral.String((string)instruction.Operand) });
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
        var writer = new CodeWriter();
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
        var writer = new CodeWriter();
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
        writer.WriteLine($"constexpr auto PaperLogger = Paper::ConstLoggerContext(\"{_modInfo.Id}\");");

        WriteOutputFile(outputDirectory, Path.Combine("include", "main.hpp"), writer.ToString());
    }

    private void GenerateMainSource(string outputDirectory)
    {
        var bodyGenerators = _hooks.Select(hook => new HookBodyGenerator(hook, _typeSystem, _configValues, _metadataIndex)).ToDictionary(generator => generator.Hook, generator => generator);

        foreach (var generator in bodyGenerators.Values)
            generator.Generate();

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

        var writer = new CodeWriter();
        writer.WriteLine("#include \"main.hpp\"");
        writer.WriteLine("#include \"scotland2/shared/modloader.h\"");
        writer.WriteLine();

        foreach (var include in includeSet.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            writer.WriteLine($"#include \"{include}\"");

        writer.WriteLine();
        writer.WriteLine($"static modloader::ModInfo modInfo{{\"{_modInfo.Id}\", \"{_modInfo.Version}\", 0}};");
        writer.WriteLine();
        writer.WriteLine("Configuration &getConfig() {");
        writer.WriteLine("    static Configuration config(modInfo);");
        writer.WriteLine("    return config;");
        writer.WriteLine("}");
        writer.WriteLine();

        foreach (var hook in _hooks)
            WriteHook(writer, hook, bodyGenerators[hook]);

        writer.WriteLine("MOD_EXTERN_FUNC void late_load() noexcept {");
        writer.WriteLine("    il2cpp_functions::Init();");
        writer.WriteLine("    PaperLogger.info(\"Installing hooks...\");");
        writer.WriteLine();

        foreach (var hook in _hooks)
            writer.WriteLine($"    INSTALL_HOOK(PaperLogger, {hook.HookName});");

        writer.WriteLine();
        writer.WriteLine("    PaperLogger.info(\"Installed all hooks!\");");
        writer.WriteLine("}");

        WriteOutputFile(outputDirectory, Path.Combine("src", "main.cpp"), writer.ToString());
    }

    private void WriteHook(CodeWriter writer, HookInfo hook, HookBodyGenerator bodyGenerator)
    {
        var returnType = _typeSystem.MapType(hook.Method.ReturnType);
        var parameters = hook.Method.Parameters.Select(parameter => $"{_typeSystem.MapType(parameter.ParameterType)} {CppName.Sanitize(parameter.Name)}").ToList();

        writer.WriteLine("MAKE_HOOK_MATCH(");
        writer.WriteLine($"    {hook.HookName},");
        writer.WriteLine($"    &{_typeSystem.MapNamespace(hook.TargetType.Namespace)}::{_typeSystem.ComposeTypeName(hook.TargetType)}::{MapTargetMethodName(hook)},");
        writer.WriteLine($"    {returnType},");
        writer.WriteLine($"    {string.Join(", ", parameters)}) {{");

        foreach (var line in bodyGenerator.Lines)
            writer.WriteLine($"    {line}");

        writer.WriteLine("}");
        writer.WriteLine();
    }

    private void WriteOutputFile(string outputDirectory, string relativePath, string content)
    {
        var fullPath = Path.Combine(outputDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        GeneratedFiles.Add(new GeneratedFile { Path = fullPath, Content = content });
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

    private static string MapTargetMethodName(HookInfo hook)
    {
        return hook.IsConstructor ? "_ctor" : hook.TargetMethod;
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

    private static string BuildUniqueConfigIdentifier(ConfigValueInfo config)
    {
        return $"{SanitizeIdentifier(config.DeclaringTypeFullName)}_{CppName.Sanitize(config.Name)}";
    }

    private static string SanitizeIdentifier(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var sanitized = new string(chars);
        if (string.IsNullOrWhiteSpace(sanitized))
            return "config";

        return char.IsDigit(sanitized[0]) ? $"_{sanitized}" : sanitized;
    }

    private static string? TryGetAutoPropertyName(string fieldName)
    {
        const string suffix = ">k__BackingField";
        if (!fieldName.StartsWith("<", StringComparison.Ordinal) || !fieldName.EndsWith(suffix, StringComparison.Ordinal))
            return null;

        return fieldName[1..^suffix.Length];
    }
}
