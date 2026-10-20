using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Transpiler;

internal sealed partial class Transpiler
{
    private List<HelperMethodEmission> BuildHelperMethodEmissions(IReadOnlyDictionary<string, string> localMethodNames)
    {
        var result = new List<HelperMethodEmission>();
        foreach (var method in _helperMethods)
        {
            var functionName = localMethodNames[method.FullName];
            var syntheticHook = new PatchDefinition
            {
                HookName = functionName,
                TargetMethod = method.Name,
                TargetType = method.DeclaringType,
                Method = method,
                IsConstructor = false,
                Phase = PatchBehavior.Full,
            };

            var translator = BuildStructuredTranslator(syntheticHook, localMethodNames);
            result.Add(new HelperMethodEmission(method, functionName, translator));
        }

        return result;
    }

    private List<CustomTypeMethodEmission> BuildCustomTypeMethodEmissions()
    {
        var result = new List<CustomTypeMethodEmission>();
        foreach (var customType in _customTypes)
        {
            foreach (var method in customType.Methods)
            {
                var syntheticHook = new PatchDefinition
                {
                    HookName = method.CppName,
                    TargetMethod = method.Method.Name,
                    TargetType = method.Method.DeclaringType,
                    Method = method.Method,
                    IsConstructor = false,
                    Phase = PatchBehavior.Full,
                };

                var translator = BuildStructuredTranslator(syntheticHook, new Dictionary<string, string>(StringComparer.Ordinal));
                result.Add(new CustomTypeMethodEmission(customType, method, translator));
            }
        }

        return result;
    }

    private IlMethodTranslator BuildStructuredTranslator(PatchDefinition hook, IReadOnlyDictionary<string, string> localMethodNames)
    {
        var translator = new IlMethodTranslator(hook, _typeSystem, _configValues, _localStaticFields, _metadataIndex, localMethodNames);
        translator.Translate();
        return translator;
    }
}
