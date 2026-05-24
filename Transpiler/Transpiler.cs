using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed class Transpiler
{
    private sealed record HookEmission(HookDefinition Hook, HookDefinition? PrefixHook, HookDefinition? PostfixHook, IlMethodTranslator? FullBody, IlMethodTranslator? PrefixBody, IlMethodTranslator? PostfixBody);

    private sealed record HelperMethodEmission(MethodDefinition Method, string FunctionName, IlMethodTranslator Body);

    private sealed record MenuButtonRegistration(MethodDefinition Method, string Text, string HoverHint);

    private sealed record GameplaySetupTabRegistration(MethodDefinition Method, string Name, int MenuTypeValue);

    private readonly string _assemblyPath;
    private readonly List<HookDefinition> _hooks = new();
    private readonly List<ConfigEntry> _configValues = new();
    private readonly List<LocalStaticFieldEntry> _localStaticFields = new();
    private readonly List<CustomTypeEntry> _customTypes = new();
    private readonly List<MethodDefinition> _helperMethods = new();
    private readonly List<MenuButtonRegistration> _menuButtons = new();
    private readonly List<GameplaySetupTabRegistration> _gameplaySetupTabs = new();
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
        LoadLocalStaticFields(type);
        LoadCustomType(type);
        LoadHooks(type);
        LoadBsmlRegistrations(type);
        LoadHelperMethods(type);

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

            defaults.TryGetValue($"property:{property.Name}", out var defaultValue);
            var defaultValueCpp = defaultValue?.Code ?? ReadNamedAttributeDefaultValue(configAttribute, property.PropertyType);

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

            defaults.TryGetValue($"field:{field.Name}", out var defaultValue);
            var defaultValueCpp = defaultValue?.Code ?? ReadNamedAttributeDefaultValue(configAttribute, field.FieldType);

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

    private Dictionary<string, StaticDefaultValue> ReadStaticDefaults(TypeDefinition type)
    {
        var result = new Dictionary<string, StaticDefaultValue>(StringComparer.Ordinal);
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
                case Code.Dup:
                    if (stack.Count > 0)
                        stack.Push(stack.Peek());
                    break;
                case Code.Newarr:
                {
                    var elementType = (TypeReference)instruction.Operand;
                    if (stack.Count == 0 || elementType.FullName != "System.String")
                    {
                        stack.Clear();
                        break;
                    }

                    var lengthExpression = stack.Pop().Code;
                    if (!int.TryParse(lengthExpression, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) || length < 0)
                    {
                        stack.Clear();
                        break;
                    }

                    stack.Push(new CppExpression
                    {
                        Code = "",
                        StringArrayElements = Enumerable.Repeat<string?>(null, length).ToList(),
                    });
                    break;
                }
                case Code.Stelem_Ref:
                {
                    if (stack.Count < 3)
                    {
                        stack.Clear();
                        break;
                    }

                    var value = stack.Pop();
                    var index = stack.Pop();
                    var array = stack.Pop();
                    if (array.StringArrayElements == null || !int.TryParse(index.Code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var elementIndex) || elementIndex < 0 || elementIndex >= array.StringArrayElements.Count)
                    {
                        stack.Clear();
                        break;
                    }

                    array.StringArrayElements[elementIndex] = value.Code;
                    break;
                }
                case Code.Stsfld:
                {
                    if (stack.Count == 0)
                        break;

                    var value = stack.Pop();
                    var field = (FieldReference)instruction.Operand;
                    var propertyName = TryGetAutoPropertyName(field.Name);
                    result[propertyName != null ? $"property:{propertyName}" : $"field:{field.Name}"] = value.StringArrayElements != null
                        ? new StaticDefaultValue { StringArrayElements = value.StringArrayElements.Select(item => item ?? CppLiteral.String("")).ToArray() }
                        : new StaticDefaultValue { Code = NormalizeDefaultValue(field.FieldType, value.Code) };
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
        writer.WriteLine("#define BS_HOOK_MATCH_UNSAFE");
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
        var localMethodNames = _helperMethods.ToDictionary(method => method.FullName, GetHelperFunctionName, StringComparer.Ordinal);
        var helperMethodEmissions = BuildHelperMethodEmissions(localMethodNames);
        var bodyGenerators = _hooks.ToDictionary(hook => hook, hook => BuildStructuredTranslator(hook, localMethodNames));

        var includeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hook in _hooks)
        {
            AddInclude(includeSet, _typeSystem.GetIncludePath(hook.TargetType));
            foreach (var parameter in hook.Method.Parameters)
                AddInclude(includeSet, _typeSystem.GetIncludePath(parameter.ParameterType));

            foreach (var include in CollectBodyIncludes(hook.Method))
                AddInclude(includeSet, include);
        }

        foreach (var helperMethod in _helperMethods)
        {
            AddInclude(includeSet, _typeSystem.GetIncludePath(helperMethod.ReturnType));
            foreach (var parameter in helperMethod.Parameters)
                AddInclude(includeSet, _typeSystem.GetIncludePath(parameter.ParameterType));

            foreach (var include in CollectBodyIncludes(helperMethod))
                AddInclude(includeSet, include);
        }

        foreach (var customType in _customTypes)
        {
            AddInclude(includeSet, _typeSystem.GetIncludePath(customType.Type.BaseType));
            foreach (var field in customType.Type.Fields)
                AddInclude(includeSet, _typeSystem.GetIncludePath(field.FieldType));
        }

        foreach (var generator in bodyGenerators.Values)
        {
            foreach (var include in generator.RequiredIncludes)
                includeSet.Add(include);
        }

        foreach (var helperMethod in helperMethodEmissions)
        {
            foreach (var include in helperMethod.Body.RequiredIncludes)
                includeSet.Add(include);
        }

        if (_menuButtons.Count > 0 || _gameplaySetupTabs.Count > 0)
            includeSet.Add("bsml/shared/BSML.hpp");

        foreach (var localInclude in CollectCurrentModuleIncludePaths())
            includeSet.Remove(localInclude);

        var hookEmissions = BuildHookEmissions(bodyGenerators);

        var writer = new CppCodeWriter();
        writer.WriteLine("#include \"main.hpp\"");
        writer.WriteLine("#include \"scotland2/shared/modloader.h\"");
        if (_customTypes.Count > 0)
        {
            writer.WriteLine("#include \"custom-types/shared/register.hpp\"");
            writer.WriteLine("#include \"custom-types/shared/macros.hpp\"");
        }
        writer.WriteLine();

        foreach (var include in includeSet.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            writer.WriteLine($"#include \"{include}\"");

        writer.WriteLine();
        writer.WriteLine($"static modloader::ModInfo modInfo{{\"{_modMetadata.Id}\", \"{_modMetadata.Version}\", 0}};");
        writer.WriteLine();

        foreach (var field in _localStaticFields)
        {
            var cppType = _typeSystem.MapType(field.Type);
            var defaultValue = field.DefaultValueCpp ?? _typeSystem.GetDefaultValue(field.Type);
            writer.WriteLine($"static {cppType} {field.CppIdentifier} = {defaultValue};");
        }

        if (_localStaticFields.Count > 0)
            writer.WriteLine();

        if (_customTypes.Count > 0)
        {
            WriteCustomTypes(writer);
            writer.WriteLine();
        }

        writer.WriteLine("Configuration &getConfig() {");
        writer.WriteLine("    static Configuration config(modInfo);");
        writer.WriteLine("    return config;");
        writer.WriteLine("}");
        writer.WriteLine();

        WriteLocalStaticInitializers(writer);

        foreach (var helperMethod in helperMethodEmissions)
            WriteHelperPrototype(writer, helperMethod);

        if (helperMethodEmissions.Count > 0)
            writer.WriteLine();

        foreach (var helperMethod in helperMethodEmissions)
            WriteHelperFunction(writer, helperMethod);

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
        if (_localStaticFields.Any(field => field.StringArrayElements != null))
            writer.WriteLine("    InitializeLocalStaticFields();");
        if (_customTypes.Count > 0)
            writer.WriteLine("    custom_types::Register::AutoRegister();");

        if (_menuButtons.Count > 0 || _gameplaySetupTabs.Count > 0)
        {
            writer.WriteLine("    BSML::Init();");

            foreach (var menuButton in _menuButtons)
                writer.WriteLine($"    BSML::Register::RegisterMenuButton({CppLiteral.CString(menuButton.Text)}, {CppLiteral.CString(menuButton.HoverHint)}, {GetHelperFunctionName(menuButton.Method)});");

            foreach (var gameplaySetupTab in _gameplaySetupTabs)
                writer.WriteLine($"    BSML::Register::RegisterGameplaySetupTab({CppLiteral.CString(gameplaySetupTab.Name)}, {GetHelperFunctionName(gameplaySetupTab.Method)}, {MapBsmlMenuType(gameplaySetupTab.MenuTypeValue)});");

            writer.WriteLine();
        }

        writer.WriteLine("    PaperLogger.info(\"Installing hooks...\");");
        writer.WriteLine();

        foreach (var emission in hookEmissions)
            writer.WriteLine($"    INSTALL_HOOK(PaperLogger, {emission.Hook.HookName});");

        writer.WriteLine();
        writer.WriteLine("    PaperLogger.info(\"Installed all hooks!\");");
        writer.WriteLine("}");

        WriteOutputFile(outputDirectory, Path.Combine("src", "main.cpp"), NormalizeGeneratedSource(writer.ToString()));
    }

    private void WriteLocalStaticInitializers(CppCodeWriter writer)
    {
        var arrayFields = _localStaticFields.Where(field => field.StringArrayElements != null).ToList();
        if (arrayFields.Count == 0)
            return;

        writer.WriteLine("static void InitializeLocalStaticFields() {");
        foreach (var field in arrayFields)
        {
            var elements = field.StringArrayElements!;
            writer.WriteLine($"    {field.CppIdentifier} = ArrayW<::StringW>({elements.Count});");
            for (var i = 0; i < elements.Count; i++)
                writer.WriteLine($"    {field.CppIdentifier}[{i}] = {elements[i]};");
        }
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private void WriteCustomTypes(CppCodeWriter writer)
    {
        foreach (var customType in _customTypes)
        {
            var fieldDefaults = customType.Fields.Where(field => field.DefaultValueCpp != null).ToList();
            writer.WriteLine($"DECLARE_CLASS_CODEGEN_DLL({customType.CppNamespace}, {customType.CppName}, {customType.BaseCppType}, \"{customType.DllName}\") {{");
            writer.WriteLine(fieldDefaults.Count == 0 ? "    DECLARE_DEFAULT_CTOR();" : "    DECLARE_CTOR(__ctor);");
            foreach (var field in customType.Fields)
                writer.WriteLine($"    DECLARE_INSTANCE_FIELD({field.CppType}, {field.Name});");
            writer.WriteLine("};");
            writer.WriteLine();
            writer.WriteLine($"DEFINE_TYPE({customType.CppNamespace}, {customType.CppName});");
            writer.WriteLine();

            if (fieldDefaults.Count == 0)
                continue;

            var qualifiedType = $"{customType.CppNamespace}::{customType.CppName}";
            writer.WriteLine($"void {qualifiedType}::__ctor() {{");
            writer.WriteLine("    INVOKE_CTOR();");
            writer.WriteLine($"    INVOKE_BASE_CTOR({qualifiedType}::___TypeRegistration::get()->baseType());");
            foreach (var field in fieldDefaults)
                writer.WriteLine($"    {field.Name} = {field.DefaultValueCpp};");
            writer.WriteLine("}");
            writer.WriteLine();
        }
    }

    private static string MapBsmlMenuType(int value)
    {
        return value switch
        {
            0 => "BSML::MenuType::None",
            1 => "BSML::MenuType::Solo",
            2 => "BSML::MenuType::Online",
            4 => "BSML::MenuType::Campaign",
            8 => "BSML::MenuType::Custom",
            15 => "BSML::MenuType::All",
            _ => $"static_cast<BSML::MenuType>({value})",
        };
    }

    private void WriteHook(CppCodeWriter writer, HookDefinition hook, IlMethodTranslator bodyGenerator)
    {
        var runtimeTargetType = ResolveRuntimeHookTargetType(hook);
        var returnType = _typeSystem.MapType(hook.Method.ReturnType);
        var parameters = BuildHookRuntimeParameters(hook, runtimeTargetType);

        writer.WriteLine("MAKE_HOOK_MATCH(");
        writer.WriteLine($"    {hook.HookName},");
        writer.WriteLine($"    &{_typeSystem.MapNamespace(runtimeTargetType.Namespace)}::{_typeSystem.ComposeTypeName(runtimeTargetType)}::{MapTargetMethodName(hook)},");
        writer.WriteLine($"    {returnType},");
        writer.WriteLine($"    {string.Join(", ", parameters)}) {{");

        WriteHookParameterCasts(writer, hook, runtimeTargetType);

        foreach (var line in bodyGenerator.Statements)
            writer.WriteLine($"    {line}");

        writer.WriteLine("}");
        writer.WriteLine();
    }

    private void WriteHookWithPrefixPostfix(CppCodeWriter writer, HookDefinition hook, HookDefinition? prefixHook, IlMethodTranslator? prefixBody, HookDefinition? postfixHook, IlMethodTranslator? postfixBody)
    {
        if (prefixHook == null && postfixHook == null)
            throw new InvalidOperationException($"Hook {hook.HookName} must have a prefix or postfix method.");

        var runtimeTargetType = ResolveRuntimeHookTargetType(hook);
        var returnType = _typeSystem.MapType(hook.Method.ReturnType);
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

        WriteHookParameterCasts(writer, hook, runtimeTargetType);

        if (prefixHook != null)
            writer.WriteLine($"    {GetHelperFunctionName(prefixHook)}({argumentList});");

        if (hook.Method.ReturnType.FullName == "System.Void")
        {
            writer.WriteLine($"    {hook.HookName}({runtimeArgumentList});");
            if (postfixHook != null)
                writer.WriteLine($"    {GetHelperFunctionName(postfixHook)}({argumentList});");
            writer.WriteLine("    return;");
        }
        else
        {
            writer.WriteLine($"    auto result = {hook.HookName}({runtimeArgumentList});");
            if (postfixHook != null)
                writer.WriteLine($"    {GetHelperFunctionName(postfixHook)}({argumentList});");
            writer.WriteLine("    return result;");
        }

        writer.WriteLine("}");
        writer.WriteLine();
    }

    private List<string> BuildHookRuntimeParameters(HookDefinition hook, TypeReference runtimeTargetType)
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

    private string BuildHookRuntimeArgumentList(HookDefinition hook, TypeReference runtimeTargetType)
    {
        return string.Join(
            ", ",
            hook.Method.Parameters.Select((parameter, index) => index == 0 ? GetHookRuntimeParameterName(hook, runtimeTargetType) : CppIdentifier.Sanitize(parameter.Name))
        );
    }

    private static string GetHookRuntimeParameterName(HookDefinition hook, TypeReference runtimeTargetType)
    {
        var selfName = CppIdentifier.Sanitize(hook.Method.Parameters[0].Name);
        return string.Equals(hook.Method.Parameters[0].ParameterType.FullName, runtimeTargetType.FullName, StringComparison.Ordinal) ? selfName : $"{selfName}Raw";
    }

    private void WriteHookParameterCasts(CppCodeWriter writer, HookDefinition hook, TypeReference runtimeTargetType)
    {
        if (hook.Method.Parameters.Count == 0)
            return;

        var expectedSelfType = hook.Method.Parameters[0].ParameterType;
        if (string.Equals(expectedSelfType.FullName, runtimeTargetType.FullName, StringComparison.Ordinal))
            return;

        var selfName = CppIdentifier.Sanitize(hook.Method.Parameters[0].Name);
        var runtimeSelfName = GetHookRuntimeParameterName(hook, runtimeTargetType);
        writer.WriteLine($"    auto {selfName} = reinterpret_cast<{_typeSystem.MapType(expectedSelfType)}>({runtimeSelfName});");
    }

    private TypeReference ResolveRuntimeHookTargetType(HookDefinition hook)
    {
        if (hook.IsConstructor)
            return hook.TargetType;

        try
        {
            var resolved = hook.TargetType.Resolve();
            while (resolved != null)
            {
                var match = resolved.Methods.FirstOrDefault(method => string.Equals(method.Name, hook.TargetMethod, StringComparison.Ordinal) && method.Parameters.Count == hook.Method.Parameters.Count - 1);
                if (match != null)
                    return _module?.ImportReference(match.DeclaringType) ?? match.DeclaringType;

                resolved = resolved.BaseType?.Resolve();
            }
        }
        catch
        {
        }

        return hook.TargetType;
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

    private void WriteHelperPrototype(CppCodeWriter writer, HelperMethodEmission helperMethod)
    {
        var returnType = _typeSystem.MapType(helperMethod.Method.ReturnType);
        var parameters = helperMethod.Method.Parameters.Select(parameter => $"{_typeSystem.MapType(parameter.ParameterType)} {CppIdentifier.Sanitize(parameter.Name)}").ToList();
        writer.WriteLine($"static {returnType} {helperMethod.FunctionName}({string.Join(", ", parameters)});");
    }

    private void WriteHelperFunction(CppCodeWriter writer, HelperMethodEmission helperMethod)
    {
        var returnType = _typeSystem.MapType(helperMethod.Method.ReturnType);
        var parameters = helperMethod.Method.Parameters.Select(parameter => $"{_typeSystem.MapType(parameter.ParameterType)} {CppIdentifier.Sanitize(parameter.Name)}").ToList();
        writer.WriteLine($"static {returnType} {helperMethod.FunctionName}({string.Join(", ", parameters)}) {{");
        foreach (var line in helperMethod.Body.Statements)
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

    private static bool IsCustomTypeAttribute(CustomAttribute attribute) => attribute.AttributeType.Name is "CustomTypeAttribute" or "CustomType";

    private static bool IsMenuButtonAttribute(CustomAttribute attribute) => attribute.AttributeType.Name is "MenuButtonAttribute" or "MenuButton";

    private static bool IsGameplaySetupTabAttribute(CustomAttribute attribute) => attribute.AttributeType.Name is "GameplaySetupTabAttribute" or "GameplaySetupTab";

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

        _customTypes.Add(
            new CustomTypeEntry
            {
                Type = type,
                CppNamespace = _typeSystem.MapNamespace(type.Namespace),
                CppName = _typeSystem.ComposeTypeName(type),
                BaseCppType = _typeSystem.MapType(type.BaseType).TrimEnd('*'),
                DllName = _module?.Assembly.Name.Name ?? type.Module.Assembly.Name.Name,
                Fields = fields,
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
        block = inlineIfRegex.Replace(block, match =>
        {
            var localName = match.Groups["name"].Value;
            var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
            return usageCount == 3 ? $"{match.Groups["ifIndent"].Value}if ({match.Groups["expr"].Value})" : match.Value;
        });

        var duplicateExprIfRegex = new Regex(@"^(?<assignIndent>\s*)(?<name>local\d+(?:_\d+)?) = (?<expr>.+);\r?\n(?<ifIndent>\s*)if \((?:\()?(?<condition>\k<expr>)(?:\))?\)", RegexOptions.Multiline);
        block = duplicateExprIfRegex.Replace(block, match =>
        {
            var localName = match.Groups["name"].Value;
            var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
            return usageCount == 2 ? $"{match.Groups["ifIndent"].Value}if ({match.Groups["expr"].Value})" : match.Value;
        });

        var wrappedDuplicateExprIfRegex = new Regex(@"^(?<assignIndent>\s*)(?<name>local\d+(?:_\d+)?) = \((?<expr>.+)\);\r?\n(?<ifIndent>\s*)if \((?:\()?(?<condition>\k<expr>)(?:\))?\)", RegexOptions.Multiline);
        block = wrappedDuplicateExprIfRegex.Replace(block, match =>
        {
            var localName = match.Groups["name"].Value;
            var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
            return usageCount == 2 ? $"{match.Groups["ifIndent"].Value}if ({match.Groups["expr"].Value})" : match.Value;
        });

        var inlineReturnRegex = new Regex(@"^(?<assignIndent>\s*)(?<name>local\d+(?:_\d+)?) = (?<expr>.+);\r?\n(?<returnIndent>\s*)return (?<returnName>\k<name>);", RegexOptions.Multiline);
        block = inlineReturnRegex.Replace(block, match =>
        {
            var localName = match.Groups["name"].Value;
            var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
            return usageCount == 3 ? $"{match.Groups["returnIndent"].Value}return {match.Groups["expr"].Value};" : match.Value;
        });

        var localAssignmentRegex = new Regex(@"^(?<indent>\s*)(?<name>local\d+(?:_\d+)?) = (?<expr>.+);\s*$", RegexOptions.Multiline);
        var changed = true;
        while (changed)
        {
            changed = false;
            block = localAssignmentRegex.Replace(block, match =>
            {
                var localName = match.Groups["name"].Value;
                var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
                if (usageCount == 2)
                {
                    changed = true;
                    return string.Empty;
                }

                return match.Value;
            });
        }

        var localDeclarationRegex = new Regex(@"^(?<indent>\s*)(?<type>[\w:<>]+(?:\s*[*&])?)\s+(?<name>local\d+(?:_\d+)?)\{\};\s*$", RegexOptions.Multiline);
        block = localDeclarationRegex.Replace(block, match =>
        {
            var localName = match.Groups["name"].Value;
            var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
            return usageCount <= 1 ? string.Empty : match.Value;
        });

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
