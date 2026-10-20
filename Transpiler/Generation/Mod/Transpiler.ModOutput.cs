using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Transpiler;

internal sealed partial class Transpiler
{
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
        var customTypeMethodEmissions = BuildCustomTypeMethodEmissions();
        var bodyGenerators = _patches.ToDictionary(hook => hook, hook => BuildStructuredTranslator(hook, localMethodNames));
        var configDefaultsInitializer = ConfigGenerator.GenerateDefaultsInitializer(_configValues, _typeSystem);
        var bsmlOutput = BsmlRegistrationGenerator.Generate(_menuButtons, _gameplaySetupTabs);

        var includeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hook in _patches)
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

            foreach (var method in customType.Methods)
            {
                AddInclude(includeSet, _typeSystem.GetIncludePath(method.Method.ReturnType));
                foreach (var parameter in method.Method.Parameters)
                    AddInclude(includeSet, _typeSystem.GetIncludePath(parameter.ParameterType));

                foreach (var include in CollectBodyIncludes(method.Method))
                    AddInclude(includeSet, include);
            }
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

        foreach (var method in customTypeMethodEmissions)
        {
            foreach (var include in method.Body.RequiredIncludes)
                includeSet.Add(include);
        }

        includeSet.UnionWith(bsmlOutput.Includes);

        foreach (var localInclude in CollectCurrentModuleIncludePaths())
            includeSet.Remove(localInclude);

        var hookEmissions = BuildPatchEmissions(bodyGenerators);

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
            WriteCustomTypes(writer, customTypeMethodEmissions);
            writer.WriteLine();
        }

        writer.WriteLine("Configuration &getConfig() {");
        writer.WriteLine("    static Configuration config(modInfo);");
        writer.WriteLine("    return config;");
        writer.WriteLine("}");
        writer.WriteLine();

        WriteLocalStaticInitializers(writer);
        if (ConfigGenerator.NeedsRuntimeInitialization(_configValues))
        {
            writer.WriteLine(configDefaultsInitializer);
            writer.WriteLine();
        }

        foreach (var helperMethod in helperMethodEmissions)
            WriteHelperPrototype(writer, helperMethod);

        if (helperMethodEmissions.Count > 0)
            writer.WriteLine();

        foreach (var helperMethod in helperMethodEmissions)
            WriteHelperFunction(writer, helperMethod);

        foreach (var emission in hookEmissions)
        {
            if (emission.ReplacementBody != null)
            {
                WriteHook(writer, emission.Target, emission.ReplacementBody);
                continue;
            }

            WriteHookWithPrefixPostfix(writer, emission.Target, emission.Before, emission.BeforeBody, emission.After, emission.AfterBody);
        }

        writer.WriteLine("MOD_EXTERN_FUNC void late_load() noexcept {");
        writer.WriteLine("    il2cpp_functions::Init();");
        if (!string.IsNullOrEmpty(configDefaultsInitializer))
            writer.WriteLine("    InitializeConfigDefaults();");
        if (_localStaticFields.Any(field => field.StringArrayElements != null))
            writer.WriteLine("    InitializeLocalStaticFields();");
        if (_customTypes.Count > 0)
            writer.WriteLine("    custom_types::Register::AutoRegister();");

        if (bsmlOutput.HasRegistrations)
        {
            foreach (var line in bsmlOutput.InitializationLines)
                writer.WriteLine(line);
            writer.WriteLine();
        }

        writer.WriteLine("    PaperLogger.info(\"Installing hooks...\");");
        writer.WriteLine();

        foreach (var emission in hookEmissions)
            writer.WriteLine($"    INSTALL_HOOK(PaperLogger, {emission.Target.HookName});");

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
}
