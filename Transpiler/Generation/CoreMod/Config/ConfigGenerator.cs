using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Transpiler;

internal static class ConfigGenerator
{
    public static string GenerateHeader(IReadOnlyList<ConfigEntry> configs, CppTypeSystem typeSystem)
    {
        var writer = new CppCodeWriter();
        writer.WriteLine("#pragma once");
        writer.WriteLine();
        writer.WriteLine("#define MOD_EXPORT __attribute__((visibility(\"default\")))");
        writer.WriteLine("#define MOD_EXTERN_FUNC extern \"C\" MOD_EXPORT");
        writer.WriteLine();
        writer.WriteLine("#include \"beatsaber-hook/shared/utils/il2cpp-utils.hpp\"");
        writer.WriteLine();

        foreach (var config in configs)
        {
            var cppType = typeSystem.MapType(config.Type);
            var defaultValue = GetGlobalInitializerValue(config, typeSystem);
            writer.WriteLine($"static {cppType} {config.CppIdentifier} = {defaultValue};");
        }

        return writer.ToString();
    }

    public static string GenerateDefaultsInitializer(IReadOnlyList<ConfigEntry> configs, CppTypeSystem typeSystem)
    {
        var runtimeConfigs = configs.Where(config => RequiresRuntimeInitialization(config.Type, config.DefaultValueCpp)).ToList();
        if (runtimeConfigs.Count == 0)
            return string.Empty;

        var writer = new CppCodeWriter();
        writer.WriteLine("static void InitializeConfigDefaults() {");
        foreach (var config in runtimeConfigs)
            writer.WriteLine($"    {config.CppIdentifier} = {config.DefaultValueCpp ?? typeSystem.GetDefaultValue(config.Type)};");
        writer.WriteLine("}");
        return writer.ToString();
    }

    public static bool NeedsRuntimeInitialization(IEnumerable<ConfigEntry> configs)
    {
        return configs.Any(config => RequiresRuntimeInitialization(config.Type, config.DefaultValueCpp));
    }

    private static string GetGlobalInitializerValue(ConfigEntry config, CppTypeSystem typeSystem)
    {
        return RequiresRuntimeInitialization(config.Type, config.DefaultValueCpp) ? typeSystem.GetDefaultValue(config.Type) : config.DefaultValueCpp ?? typeSystem.GetDefaultValue(config.Type);
    }

    private static bool RequiresRuntimeInitialization(TypeReference type, string? defaultValue)
    {
        return type.FullName == "System.String" && !string.IsNullOrWhiteSpace(defaultValue) && defaultValue.Contains("newcsstr", StringComparison.Ordinal);
    }
}
