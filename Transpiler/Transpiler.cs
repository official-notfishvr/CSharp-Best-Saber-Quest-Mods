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

                    stack.Push(new CppExpression { Code = "", StringArrayElements = Enumerable.Repeat<string?>(null, length).ToList() });
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
                    result[propertyName != null ? $"property:{propertyName}" : $"field:{field.Name}"] =
                        value.StringArrayElements != null ? new StaticDefaultValue { StringArrayElements = value.StringArrayElements.Select(item => item ?? CppLiteral.String("")).ToArray() } : new StaticDefaultValue { Code = NormalizeDefaultValue(field.FieldType, value.Code) };
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
            var defaultValue = GetGlobalInitializerValue(config.Type, config.DefaultValueCpp);
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
        WriteConfigInitializers(writer);

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
        if (_configValues.Any(config => RequiresRuntimeInitialization(config.Type, config.DefaultValueCpp)))
            writer.WriteLine("    InitializeConfigDefaults();");
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

    private void WriteConfigInitializers(CppCodeWriter writer)
    {
        var runtimeConfigs = _configValues.Where(config => RequiresRuntimeInitialization(config.Type, config.DefaultValueCpp)).ToList();
        if (runtimeConfigs.Count == 0)
            return;

        writer.WriteLine("static void InitializeConfigDefaults() {");
        foreach (var config in runtimeConfigs)
        {
            var defaultValue = config.DefaultValueCpp ?? _typeSystem.GetDefaultValue(config.Type);
            writer.WriteLine($"    {config.CppIdentifier} = {defaultValue};");
        }
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private string GetGlobalInitializerValue(TypeReference type, string? defaultValue)
    {
        return RequiresRuntimeInitialization(type, defaultValue) ? _typeSystem.GetDefaultValue(type) : defaultValue ?? _typeSystem.GetDefaultValue(type);
    }

    private static bool RequiresRuntimeInitialization(TypeReference type, string? defaultValue)
    {
        return type.FullName == "System.String" && !string.IsNullOrWhiteSpace(defaultValue) && defaultValue.Contains("newcsstr", StringComparison.Ordinal);
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

        var hasRuntimeSelfCheck = RequiresHookSelfRuntimeCheck(hook, runtimeTargetType);
        WriteHookParameterCasts(writer, hook, runtimeTargetType, defineMatchFlag: hasRuntimeSelfCheck);

        if (prefixHook != null)
        {
            if (hasRuntimeSelfCheck)
                writer.WriteLine($"    if (hookSelfMatches) {GetHelperFunctionName(prefixHook)}({argumentList});");
            else
                writer.WriteLine($"    {GetHelperFunctionName(prefixHook)}({argumentList});");
        }

        if (hook.Method.ReturnType.FullName == "System.Void")
        {
            writer.WriteLine($"    {hook.HookName}({runtimeArgumentList});");
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
            writer.WriteLine($"    auto result = {hook.HookName}({runtimeArgumentList});");
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
        return string.Join(", ", hook.Method.Parameters.Select((parameter, index) => index == 0 ? GetHookRuntimeParameterName(hook, runtimeTargetType) : CppIdentifier.Sanitize(parameter.Name)));
    }

    private static string GetHookRuntimeParameterName(HookDefinition hook, TypeReference runtimeTargetType)
    {
        var selfName = CppIdentifier.Sanitize(hook.Method.Parameters[0].Name);
        return string.Equals(hook.Method.Parameters[0].ParameterType.FullName, runtimeTargetType.FullName, StringComparison.Ordinal) ? selfName : $"{selfName}Raw";
    }

    private void WriteHookParameterCasts(CppCodeWriter writer, HookDefinition hook, TypeReference runtimeTargetType, bool defineMatchFlag)
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

    private bool RequiresHookSelfRuntimeCheck(HookDefinition hook, TypeReference runtimeTargetType)
    {
        return hook.Method.Parameters.Count > 0 && !string.Equals(hook.Method.Parameters[0].ParameterType.FullName, runtimeTargetType.FullName, StringComparison.Ordinal);
    }

    private string BuildHookSelfMatchesExpression(HookDefinition hook, TypeReference runtimeTargetType)
    {
        var expectedSelfType = hook.Method.Parameters[0].ParameterType;
        var runtimeSelfName = GetHookRuntimeParameterName(hook, runtimeTargetType);
        return $"{runtimeSelfName} != nullptr && ::il2cpp_functions::class_is_assignable_from(classof({_typeSystem.MapType(expectedSelfType)}), reinterpret_cast<Il2CppObject*>({runtimeSelfName})->klass)";
    }

    private void WriteOriginalFallback(CppCodeWriter writer, HookDefinition hook, TypeReference runtimeTargetType, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 4);
        var runtimeArgumentList = BuildHookRuntimeArgumentList(hook, runtimeTargetType);
        if (hook.Method.ReturnType.FullName == "System.Void")
        {
            writer.WriteLine($"{indent}{hook.HookName}({runtimeArgumentList});");
            writer.WriteLine($"{indent}return;");
        }
        else
        {
            writer.WriteLine($"{indent}return {hook.HookName}({runtimeArgumentList});");
        }
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
        catch { }

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
}
