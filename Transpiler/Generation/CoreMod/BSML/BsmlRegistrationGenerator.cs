using System.Collections.Generic;
using System.Linq;

namespace Transpiler;

internal sealed record MenuButtonRegistration(string FunctionName, string Text, string HoverHint);

internal sealed record GameplaySetupTabRegistration(string FunctionName, string Name, int MenuTypeValue);

internal sealed record BsmlRegistrationOutput(IReadOnlyList<string> Includes, IReadOnlyList<string> InitializationLines)
{
    public bool HasRegistrations => InitializationLines.Count > 0;
}

internal static class BsmlRegistrationGenerator
{
    public static BsmlRegistrationOutput Generate(IReadOnlyList<MenuButtonRegistration> menuButtons, IReadOnlyList<GameplaySetupTabRegistration> gameplaySetupTabs)
    {
        if (menuButtons.Count == 0 && gameplaySetupTabs.Count == 0)
            return new BsmlRegistrationOutput(System.Array.Empty<string>(), System.Array.Empty<string>());

        var lines = new List<string> { "    BSML::Init();" };
        lines.AddRange(menuButtons.Select(button => $"    BSML::Register::RegisterMenuButton({CppLiteral.CString(button.Text)}, {CppLiteral.CString(button.HoverHint)}, {button.FunctionName});"));
        lines.AddRange(gameplaySetupTabs.Select(tab => $"    BSML::Register::RegisterGameplaySetupTab({CppLiteral.CString(tab.Name)}, {tab.FunctionName}, {MapMenuType(tab.MenuTypeValue)});"));
        return new BsmlRegistrationOutput(new[] { "bsml/shared/BSML.hpp" }, lines);
    }

    private static string MapMenuType(int value)
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
}
