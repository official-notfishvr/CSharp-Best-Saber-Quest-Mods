using System.Globalization;
using System.Text;

namespace Transpiler;

internal sealed record BsmlQuestOutput(string SupportCode, IReadOnlyList<string> Includes, IReadOnlyList<string> StartupLines, IReadOnlyList<QuestConfigField> ConfigFields, IReadOnlyList<ApiCallMapping> ApiMappings);

internal static class BsmlQuestEmitter
{
    public static BsmlQuestOutput Emit(IReadOnlyList<BsmlSettingsLowerer.Menu> menus, string sourceAssemblyName)
    {
        if (menus.Count == 0)
            return new BsmlQuestOutput(string.Empty, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<QuestConfigField>(), Array.Empty<ApiCallMapping>());

        var code = new StringBuilder();
        var includes = new HashSet<string>(StringComparer.Ordinal)
        {
            "BSML.hpp",
            "BSMLDataCache.hpp",
            "System/Collections/Generic/List_1.hpp",
            "System/Object.hpp",
            "System/String.hpp",
            "custom-types/shared/macros.hpp",
            "custom-types/shared/register.hpp",
            "beatsaber-hook/shared/utils/il2cpp-utils.hpp",
        };
        var startup = new List<string> { "BSML::Init();", "custom_types::Register::AutoRegister();" };
        var configFields = new Dictionary<string, QuestConfigField>(StringComparer.Ordinal);
        var apiMappings = new Dictionary<string, ApiCallMapping>(StringComparer.Ordinal);

        startup.Add("if (!settingsRegistered) {");
        for (var index = 0; index < menus.Count; index++)
        {
            var menu = menus[index];
            if (menu.GenericBindings is not null)
            {
                EmitGenericMenu(code, includes, startup, menu, index, sourceAssemblyName);
                foreach (var action in menu.GenericBindings.Actions)
                {
                    foreach (var mapping in action.ApiMappings)
                    {
                        apiMappings.TryAdd(mapping.PublicHeader + "|" + mapping.QuestMethod, mapping);
                        includes.Add(mapping.PublicHeader);
                    }
                    foreach (var field in action.ConfigFields)
                    {
                        if (configFields.TryGetValue(field.Name, out var priorActionField) && priorActionField != field)
                            throw new InvalidOperationException($"Quest config field name collision: {field.Name}.");
                        configFields[field.Name] = field;
                    }
                }
                continue;
            }
            var bindings = menu.Bindings ?? throw new InvalidOperationException($"BSML resource '{menu.ResourceName}' has no lowered host bindings.");
            var hostType = $"GeneratedBsmlHost{index}";
            var choices = bindings.ChoicesMember;
            var value = bindings.ValueMember;
            var configParts = bindings.ConfigInfo.Split('|');
            if (configParts.Length != 3)
                throw new InvalidOperationException($"BSML resource '{menu.ResourceName}': config binding metadata is incomplete.");
            var config = new QuestConfigField(configParts[0], "int32_t", configParts[1], configParts[2]);
            if (configFields.TryGetValue(config.Name, out var previous) && previous != config)
                throw new InvalidOperationException($"BSML config field collision for '{config.Name}'.");
            configFields[config.Name] = config;

            code.AppendLine($"DECLARE_CLASS_CODEGEN_DLL(TranspiledBSML, {hostType}, System::Object, \"{Escape(sourceAssemblyName)}\") {{");
            code.AppendLine("    DECLARE_CTOR(__ctor);");
            code.AppendLine($"    DECLARE_INSTANCE_FIELD(System::Collections::Generic::List_1<::StringW>*, {choices});");
            code.AppendLine($"    DECLARE_INSTANCE_METHOD(::StringW, get_{value});");
            code.AppendLine($"    DECLARE_INSTANCE_METHOD(void, set_{value}, ::StringW selected);");
            code.AppendLine("};");
            code.AppendLine($"DEFINE_TYPE(TranspiledBSML, {hostType});");
            code.AppendLine($"static TranspiledBSML::{hostType}* bsmlHost{index} = nullptr;");
            code.AppendLine();

            code.AppendLine($"void TranspiledBSML::{hostType}::__ctor() {{");
            code.AppendLine("    INVOKE_CTOR();");
            code.AppendLine($"    INVOKE_BASE_CTOR(TranspiledBSML::{hostType}::___TypeRegistration::get()->baseType());");
            code.AppendLine($"    {choices} = System::Collections::Generic::List_1<::StringW>::New_ctor();");
            foreach (var choice in bindings.Options)
                code.AppendLine($"    {choices}->Add(il2cpp_utils::newcsstr(\"{Escape(choice)}\"));");
            code.AppendLine("}");
            code.AppendLine();

            code.AppendLine($"::StringW TranspiledBSML::{hostType}::get_{value}() {{");
            code.AppendLine($"    const auto selected = getModConfig().{config.Name}.GetValue();");
            code.AppendLine($"    if (selected < 0 || selected >= {choices}->get_Count()) return il2cpp_utils::newcsstr(\"\");");
            code.AppendLine($"    return {choices}->get_Item(selected);");
            code.AppendLine("}");
            code.AppendLine();

            code.AppendLine($"void TranspiledBSML::{hostType}::set_{value}(::StringW selected) {{");
            code.AppendLine("    int32_t selectedIndex = -1;");
            code.AppendLine($"    for (int32_t index = 0; index < {choices}->get_Count(); ++index) {{");
            code.AppendLine($"        if ({choices}->get_Item(index) == selected) {{ selectedIndex = index; break; }}");
            code.AppendLine("    }");
            code.AppendLine($"    getModConfig().{config.Name}.SetValue(selectedIndex);");
            code.AppendLine("}");
            code.AppendLine();

            code.AppendLine($"BSML_DATACACHE({menu.DataToken}) {{");
            var bytes = Encoding.UTF8.GetBytes(menu.Markup);
            code.AppendLine($"    const uint8_t content[] = {{{string.Join(", ", bytes.Select(b => b.ToString(CultureInfo.InvariantCulture)))}}};");
            code.AppendLine("    ArrayW<uint8_t> result(sizeof(content));");
            code.AppendLine("    for (size_t index = 0; index < sizeof(content); ++index) result[index] = content[index];");
            code.AppendLine("    return result;");
            code.AppendLine("}");
            code.AppendLine();

            startup.Add($"    bsmlHost{index} = TranspiledBSML::{hostType}::New_ctor();");
            startup.Add($"    settingsRegistered = BSML::Register::RegisterSettingsMenu(\"{Escape(menu.Title)}\", \"{Escape(menu.DataKey)}\", bsmlHost{index}) || settingsRegistered;");
        }

        startup.Add("}");
        return new BsmlQuestOutput(code.ToString(), includes.ToArray(), startup, configFields.Values.ToArray(), apiMappings.Values.ToArray());
    }

    private static void EmitGenericMenu(StringBuilder code, HashSet<string> includes, List<string> startup, BsmlSettingsLowerer.Menu menu, int index, string sourceAssemblyName)
    {
        var bindings = menu.GenericBindings!;
        var hostType = $"GeneratedBsmlHost{index}";
        includes.Add("System/Boolean.hpp");
        includes.Add("System/Int32.hpp");
        includes.Add("System/Single.hpp");
        includes.Add("UnityEngine/Color.hpp");

        code.AppendLine($"DECLARE_CLASS_CODEGEN_DLL(TranspiledBSML, {hostType}, System::Object, \"{Escape(sourceAssemblyName)}\") {{");
        code.AppendLine("    DECLARE_CTOR(__ctor);");
        foreach (var value in bindings.Values)
        {
            var storage = "bsml_value_" + value.GeneratedName[5..];
            code.AppendLine($"    DECLARE_INSTANCE_FIELD({value.CppType}, {storage});");
            code.AppendLine($"    DECLARE_INSTANCE_METHOD({value.CppType}, get_{value.GeneratedName});");
            if (!value.IsChoices)
                code.AppendLine($"    DECLARE_INSTANCE_METHOD(void, set_{value.GeneratedName}, {value.CppType} value);");
        }
        foreach (var action in bindings.Actions)
            code.AppendLine($"    DECLARE_INSTANCE_METHOD(void, {action.GeneratedName});");
        code.AppendLine("};");
        code.AppendLine($"DEFINE_TYPE(TranspiledBSML, {hostType});");
        code.AppendLine($"static TranspiledBSML::{hostType}* bsmlHost{index} = nullptr;");
        code.AppendLine();

        code.AppendLine($"void TranspiledBSML::{hostType}::__ctor() {{");
        code.AppendLine("    INVOKE_CTOR();");
        code.AppendLine($"    INVOKE_BASE_CTOR(TranspiledBSML::{hostType}::___TypeRegistration::get()->baseType());");
        foreach (var value in bindings.Values)
        {
            var storage = "bsml_value_" + value.GeneratedName[5..];
            if (value.IsChoices)
            {
                code.AppendLine($"    {storage} = System::Collections::Generic::List_1<System::Object*>::New_ctor();");
                foreach (var choice in value.Options)
                    code.AppendLine($"    {storage}->Add(reinterpret_cast<System::Object*>(il2cpp_utils::newcsstr(\"{Escape(choice)}\")));");
            }
            else
                code.AppendLine($"    {storage} = {value.InitialValue};");
        }
        code.AppendLine("}");
        code.AppendLine();

        foreach (var value in bindings.Values)
        {
            var storage = "bsml_value_" + value.GeneratedName[5..];
            code.AppendLine($"{value.CppType} TranspiledBSML::{hostType}::get_{value.GeneratedName}() {{ return {storage}; }}");
            if (!value.IsChoices)
                code.AppendLine($"void TranspiledBSML::{hostType}::set_{value.GeneratedName}({value.CppType} value) {{ {storage} = value; }}");
            code.AppendLine();
        }
        foreach (var action in bindings.Actions)
        {
            code.AppendLine($"void TranspiledBSML::{hostType}::{action.GeneratedName}() {{");
            foreach (var line in action.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                code.AppendLine("    " + line);
            code.AppendLine("}");
            code.AppendLine();
        }

        EmitDataCache(code, menu);
        startup.Add($"    bsmlHost{index} = TranspiledBSML::{hostType}::New_ctor();");
        startup.Add($"    settingsRegistered = BSML::Register::RegisterSettingsMenu(\"{Escape(menu.Title)}\", \"{Escape(menu.DataKey)}\", bsmlHost{index}) || settingsRegistered;");
    }

    private static void EmitDataCache(StringBuilder code, BsmlSettingsLowerer.Menu menu)
    {
        code.AppendLine($"BSML_DATACACHE({menu.DataToken}) {{");
        var bytes = Encoding.UTF8.GetBytes(menu.Markup);
        code.AppendLine($"    const uint8_t content[] = {{{string.Join(", ", bytes.Select(b => b.ToString(CultureInfo.InvariantCulture)))}}};");
        code.AppendLine("    ArrayW<uint8_t> result(sizeof(content));");
        code.AppendLine("    for (size_t index = 0; index < sizeof(content); ++index) result[index] = content[index];");
        code.AppendLine("    return result;");
        code.AppendLine("}");
        code.AppendLine();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
}
