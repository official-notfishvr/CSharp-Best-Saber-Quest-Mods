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
    private readonly Dictionary<string, LocalStaticFieldEntry> _localStaticFieldsByField;
    private readonly IReadOnlyDictionary<string, string> _localMethodNames;
    private readonly Stack<Dictionary<int, string>> _continueLabelScopes = new();
    private readonly Stack<HashSet<int>> _breakTargetScopes = new();
    private readonly HashSet<int> _declaredLocals = new();
    private readonly List<string> _temporaryDeclarations = new();
    private readonly Stack<CppExpression> _stack = new();
    private readonly TypeMetadataIndex _metadataIndex;
    private readonly CppTypeSystem _typeSystem;
    private readonly MethodDefinition _method;
    private readonly IList<Instruction> _instructions;
    private Dictionary<int, string>? _gotoLabels;
    private bool _useGotoFlow;
    private int _loopLabelCounter;
    private int _temporaryCounter;

    public IlMethodTranslator(HookDefinition hook, CppTypeSystem typeSystem, IEnumerable<ConfigEntry> configValues, IEnumerable<LocalStaticFieldEntry> localStaticFields, TypeMetadataIndex metadataIndex, IReadOnlyDictionary<string, string>? localMethodNames = null)
    {
        Hook = hook;
        _typeSystem = typeSystem;
        _metadataIndex = metadataIndex;
        _localMethodNames = localMethodNames ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _method = hook.Method;
        _instructions = _method.Body != null ? _method.Body.Instructions : Array.Empty<Instruction>();
        _localNames = BuildLocalNameMap(_method);
        _parameterNames = _method.Parameters.ToDictionary(parameter => parameter.Index, parameter => CppIdentifier.Sanitize(parameter.Name, $"arg{parameter.Index}"));
        _instructionIndices = _instructions.Select((instruction, index) => (instruction, index)).ToDictionary(item => item.instruction, item => item.index);
        _configByGetter = configValues.ToDictionary(config => BuildConfigAccessorKey(config.DeclaringTypeFullName, $"get_{config.Name}"), config => config, StringComparer.Ordinal);
        _configBySetter = configValues.ToDictionary(config => BuildConfigAccessorKey(config.DeclaringTypeFullName, $"set_{config.Name}"), config => config, StringComparer.Ordinal);
        _configByField = configValues.ToDictionary(config => BuildConfigAccessorKey(config.DeclaringTypeFullName, config.Name), config => config, StringComparer.Ordinal);
        _localStaticFieldsByField = localStaticFields.ToDictionary(field => BuildConfigAccessorKey(field.DeclaringTypeFullName, field.Name), field => field, StringComparer.Ordinal);
    }

    public HookDefinition Hook { get; }
    public List<string> Statements { get; } = new();
    public HashSet<string> RequiredIncludes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Translate()
    {
        if (!_method.HasBody || _method.Body == null)
            return;

        for (var i = 0; i < _method.Body.Variables.Count; i++)
        {
            var variable = _method.Body.Variables[i];
            if (variable.VariableType is ByReferenceType)
                continue;

            RequiredInclude(variable.VariableType);
            var declaration = ShouldValueInitializeLocal(variable.VariableType) ? $"{_typeSystem.MapType(variable.VariableType)} {GetLocalName(i)}{{}};" : $"{_typeSystem.MapType(variable.VariableType)} {GetLocalName(i)};";
            AppendLine(0, declaration);
            _declaredLocals.Add(i);
        }

        if (_declaredLocals.Count > 0)
            AppendLine(0);

        TranslateRange(0, _instructions.Count, 0);
        InsertTemporaryDeclarations();

        while (Statements.Count > 0 && string.IsNullOrWhiteSpace(Statements[^1]))
            Statements.RemoveAt(Statements.Count - 1);

        if (_method.ReturnType.FullName == "System.Void" && Statements.Count > 0 && Statements[^1].Trim() == "return;")
            Statements.RemoveAt(Statements.Count - 1);
    }

    public void TranslateUnstructured()
    {
        if (!_method.HasBody || _method.Body == null)
            return;

        _useGotoFlow = true;
        _gotoLabels = CollectGotoLabels();

        for (var i = 0; i < _method.Body.Variables.Count; i++)
        {
            var variable = _method.Body.Variables[i];
            if (variable.VariableType is ByReferenceType)
                continue;

            RequiredInclude(variable.VariableType);
            var declaration = ShouldValueInitializeLocal(variable.VariableType) ? $"{_typeSystem.MapType(variable.VariableType)} {GetLocalName(i)}{{}};" : $"{_typeSystem.MapType(variable.VariableType)} {GetLocalName(i)};";
            AppendLine(0, declaration);
            _declaredLocals.Add(i);
        }

        if (_declaredLocals.Count > 0)
            AppendLine(0);

        for (var index = 0; index < _instructions.Count; index++)
        {
            if (_gotoLabels.TryGetValue(index, out var label))
                AppendLine(0, $"{label}:;");

            EmitInstruction(_instructions[index], 0);
        }

        InsertTemporaryDeclarations();

        while (Statements.Count > 0 && string.IsNullOrWhiteSpace(Statements[^1]))
            Statements.RemoveAt(Statements.Count - 1);
    }

    private Dictionary<int, string> CollectGotoLabels()
    {
        var result = new Dictionary<int, string>();
        foreach (var instruction in _instructions)
        {
            if (instruction.Operand is not Instruction targetInstruction)
                continue;

            if (!_instructionIndices.TryGetValue(targetInstruction, out var targetIndex))
                continue;

            if (!result.ContainsKey(targetIndex))
                result[targetIndex] = $"label_{targetIndex}";
        }

        return result;
    }

    private void InsertTemporaryDeclarations()
    {
        if (_temporaryDeclarations.Count == 0)
            return;

        var insertIndex = 0;
        while (insertIndex < Statements.Count && !string.IsNullOrWhiteSpace(Statements[insertIndex]))
            insertIndex++;

        Statements.InsertRange(insertIndex, _temporaryDeclarations);
        if (insertIndex < Statements.Count && !string.IsNullOrWhiteSpace(Statements[insertIndex + _temporaryDeclarations.Count]))
            Statements.Insert(insertIndex + _temporaryDeclarations.Count, "");
    }
}
