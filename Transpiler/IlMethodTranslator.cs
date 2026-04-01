using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed partial class IlMethodTranslator
{
    private readonly record struct TranslationSnapshot(CppExpression[] StackValues, int LineCount, int[] DeclaredLocals);

    private readonly Dictionary<int, string> _localNames;
    private readonly Dictionary<int, string> _parameterNames;
    private readonly Dictionary<Instruction, int> _instructionIndices;
    private readonly Dictionary<string, ConfigEntry> _configByGetter;
    private readonly Dictionary<string, ConfigEntry> _configBySetter;
    private readonly Dictionary<string, ConfigEntry> _configByField;
    private readonly HashSet<int> _declaredLocals = new();
    private readonly Stack<CppExpression> _stack = new();
    private readonly TypeMetadataIndex _metadataIndex;
    private readonly CppTypeSystem _typeSystem;
    private readonly MethodDefinition _method;
    private readonly IList<Instruction> _instructions;

    public IlMethodTranslator(HookDefinition hook, CppTypeSystem typeSystem, IEnumerable<ConfigEntry> configValues, TypeMetadataIndex metadataIndex)
    {
        Hook = hook;
        _typeSystem = typeSystem;
        _metadataIndex = metadataIndex;
        _method = hook.Method;
        _instructions = _method.Body != null ? _method.Body.Instructions : Array.Empty<Instruction>();
        _localNames = BuildLocalNameMap(_method);
        _parameterNames = _method.Parameters.ToDictionary(parameter => parameter.Index, parameter => CppIdentifier.Sanitize(parameter.Name, $"arg{parameter.Index}"));
        _instructionIndices = _instructions.Select((instruction, index) => (instruction, index)).ToDictionary(item => item.instruction, item => item.index);
        _configByGetter = configValues.ToDictionary(config => BuildConfigAccessorKey(config.DeclaringTypeFullName, $"get_{config.Name}"), config => config, StringComparer.Ordinal);
        _configBySetter = configValues.ToDictionary(config => BuildConfigAccessorKey(config.DeclaringTypeFullName, $"set_{config.Name}"), config => config, StringComparer.Ordinal);
        _configByField = configValues.ToDictionary(config => BuildConfigAccessorKey(config.DeclaringTypeFullName, config.Name), config => config, StringComparer.Ordinal);
    }

    public HookDefinition Hook { get; }
    public List<string> Statements { get; } = new();
    public HashSet<string> RequiredIncludes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Translate()
    {
        if (!_method.HasBody || _method.Body == null)
            return;

        TranslateRange(0, _instructions.Count, 0);

        while (Statements.Count > 0 && string.IsNullOrWhiteSpace(Statements[^1]))
            Statements.RemoveAt(Statements.Count - 1);

        if (_method.ReturnType.FullName == "System.Void" && Statements.Count > 0 && Statements[^1].Trim() == "return;")
            Statements.RemoveAt(Statements.Count - 1);
    }
}
