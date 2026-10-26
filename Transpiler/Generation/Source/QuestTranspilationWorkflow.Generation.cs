using Mono.Cecil;

namespace Transpiler;

internal static partial class QuestTranspilationWorkflow
{
    static string HookTargetKey(TargetMapping mapping) => string.Join("|", mapping.QuestType, mapping.QuestMethod, mapping.QuestReturn, mapping.QuestStatic, string.Join(",", mapping.QuestParameters));

    static string GenerateGroupedHook(TargetMapping mapping, IReadOnlyList<TranslatedHarmonyPatch> patches)
    {
        var hookName = mapping.HookName;
        var parameters = mapping.QuestParameters.Select((type, index) => type + " arg" + index).ToArray();
        var receiverDeclaration = mapping.QuestStatic ? "" : "::" + mapping.QuestType + "* self";
        var declaration = string.Join(", ", (mapping.QuestStatic ? Array.Empty<string>() : new[] { receiverDeclaration }).Concat(parameters));
        var arguments = string.Join(", ", (mapping.QuestStatic ? Array.Empty<string>() : new[] { "self" }).Concat(mapping.QuestParameters.Select((_, index) => "arg" + index)));
        var prefixes = patches.Where(patch => patch.Patch.Kind == HarmonyPatchKind.Prefix).ToArray();
        var postfixes = patches.Where(patch => patch.Patch.Kind == HarmonyPatchKind.Postfix).ToArray();
        var hasBooleanPrefix = prefixes.Any(patch => patch.Patch.Method.ReturnType.MetadataType == MetadataType.Boolean);
        var body = new List<string>();
        if (hasBooleanPrefix)
            body.Add("bool runOriginal = true;");
        if (mapping.QuestReturn != "void" && hasBooleanPrefix)
            body.Add($"{mapping.QuestReturn} result{{}};");

        foreach (var prefix in prefixes)
        {
            var statements = RenderPatchBody(prefix.Statements);
            if (prefix.Patch.Method.ReturnType.MetadataType == MetadataType.Boolean)
                body.Add($"runOriginal = ([&]() -> bool {{\n{IndentCpp(statements)}\n    }}()) && runOriginal;");
            else
                body.Add($"[&]() {{\n{IndentCpp(statements)}\n    }}();");
        }

        if (mapping.QuestReturn == "void")
            body.Add(hasBooleanPrefix ? $"if (runOriginal) {hookName}({arguments});" : $"{hookName}({arguments});");
        else
            body.Add(hasBooleanPrefix ? $"if (runOriginal) result = {hookName}({arguments});" : $"auto result = {hookName}({arguments});");

        foreach (var postfix in postfixes)
            body.Add($"[&]() {{\n{IndentCpp(RenderPatchBody(postfix.Statements))}\n    }}();");
        if (mapping.QuestReturn != "void")
            body.Add("return result;");

        return $$"""
            MAKE_HOOK_MATCH({{hookName}}, &::{{mapping.QuestType}}::{{mapping.QuestMethod}}, {{mapping.QuestReturn}},
                {{declaration}}) {
                {{string.Join("\n    ", body)}}
            }
            """;
    }

    static string RenderPatchBody(IReadOnlyList<QuestLogStatement> statements) =>
        string.Join(
            "\n",
            statements
                .Where(statement => statement.RawCpp != "")
                .Select(statement =>
                    statement.RawCpp is not null ? statement.RawCpp
                    : statement.QuestExpression is null ? $"PaperLogger.{statement.QuestLevel}(\"{EscapeCpp(statement.Message)}\");"
                    : $"PaperLogger.{statement.QuestLevel}(\"{{}}\", {statement.QuestExpression});"
                )
        );

    static string GenerateSource(
        string hooks,
        IEnumerable<string> hookNames,
        IEnumerable<string> includes,
        ModMetadata metadata,
        bool usesQuestConfig = false,
        IEnumerable<QuestStaticField>? staticFields = null,
        string generatedSupport = "",
        IEnumerable<string>? startupLines = null,
        int bsmlMenuCount = 0,
        bool hasUnload = false
    ) =>
        $$"""
        #include "main.hpp"
        #include "scotland2/shared/modloader.h"
        #include <cstddef>
        #include <cstdint>
        #include <cmath>
        #include <vector>
        #include <string_view>
        {{(usesQuestConfig ? "#include \"TranspiledModConfig.hpp\"\n" : "")}}{{string.Join("\n", includes.Select(include => "#include \"" + include + "\""))}}

        static modloader::ModInfo modInfo{"{{EscapeCpp(metadata.Id)}}", "{{EscapeCpp(metadata.Version)}}", 0};
        {{(bsmlMenuCount > 0 ? "static bool settingsRegistered = false;\n" : "")}}
        {{string.Join("\n", (staticFields ?? Array.Empty<QuestStaticField>()).Select(field => field.Declaration))}}
        {{generatedSupport}}
        {{hooks}}

        MOD_EXTERN_FUNC void late_load() noexcept {
            il2cpp_functions::Init();
        {{(usesQuestConfig ? "    ConfigUtils::ConfigParent<ModConfig_t>::Init(modInfo);\n" : "")}}{{string.Join("\n", (startupLines ?? Array.Empty<string>()).Select(line => "    " + line + "\n"))}}{{string.Join("\n", hookNames.Select(name => "    INSTALL_HOOK(PaperLogger, " + name + ");"))}}
        }
        {{(
            hasUnload
                ? "\nMOD_EXTERN_FUNC void unload() noexcept {\n"
                    + (
                        bsmlMenuCount > 0
                            ? "    if (settingsRegistered) {\n" + string.Join("\n", Enumerable.Range(0, bsmlMenuCount).Select(index => $"        if (bsmlHost{index} != nullptr) BSML::Register::UnRegisterSettingsMenu(bsmlHost{index});")) + "\n        settingsRegistered = false;\n    }\n"
                            : ""
                    )
                    + "}\n"
                : ""
        )}}
        """;

    static string GenerateConfigHeader(IEnumerable<QuestConfigField> fields) =>
        "#pragma once\n#include \"config-utils/shared/config-utils.hpp\"\nDECLARE_CONFIG(ModConfig) {\n" + string.Join("\n", fields.Select(field => $"    CONFIG_VALUE({field.Name}, {field.CppType}, \"{EscapeCpp(field.Label)}\", {field.DefaultValue});")) + "\n};\n";

    static string IndentCpp(string code) => string.Join("\n", code.Split('\n').Select(line => "        " + line));

    static string EscapeCpp(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
}
