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
    private void LoadBsmlRegistrations(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            foreach (var attribute in method.CustomAttributes.Where(IsMenuButtonAttribute))
            {
                ValidateMenuButtonMethod(method);
                _menuButtons.Add(
                    new MenuButtonRegistration(GetHelperFunctionName(method), attribute.ConstructorArguments.Count > 0 ? attribute.ConstructorArguments[0].Value?.ToString() ?? "" : "", attribute.ConstructorArguments.Count > 1 ? attribute.ConstructorArguments[1].Value?.ToString() ?? "" : "")
                );
                AddHelperMethod(method);
            }

            foreach (var attribute in method.CustomAttributes.Where(IsGameplaySetupTabAttribute))
            {
                ValidateGameplaySetupTabMethod(method);
                _gameplaySetupTabs.Add(new GameplaySetupTabRegistration(GetHelperFunctionName(method), attribute.ConstructorArguments.Count > 0 ? attribute.ConstructorArguments[0].Value?.ToString() ?? "" : "", ReadNamedAttributeInt32(attribute, "MenuType", 15)));
                AddHelperMethod(method);
            }
        }
    }
}
