using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed class IlTranslationContext
{
    public IlTranslationContext(MethodDefinition method, MappingCatalog catalog, IReadOnlyDictionary<int, QuestValue> arguments, string nativeReturnType, HashSet<string>? activeLocalMethods = null)
    {
        Method = method;
        Catalog = catalog;
        Arguments = arguments;
        NativeReturnType = nativeReturnType;
        ActiveLocalMethods = activeLocalMethods ?? new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < method.Body.Variables.Count; i++)
            Locals[i] = new QuestValue($"il_local_{i}", method.Body.Variables[i].VariableType.FullName, false);
        InstructionIndexes = method.Body.Instructions.Select((instruction, index) => (instruction, index)).ToDictionary(pair => pair.instruction, pair => pair.index);
        foreach (var instruction in method.Body.Instructions.Where(instruction => instruction.OpCode.Code is Code.Ldsfld or Code.Stsfld or Code.Ldsflda))
        {
            var fieldReference = (FieldReference)instruction.Operand;
            var field = fieldReference.Resolve() ?? throw new InvalidOperationException($"Could not resolve static field {fieldReference.FullName} in {method.FullName}.");
            if (!field.IsStatic || field.Module != method.Module || !IsPrimitive(field.FieldType.FullName))
                throw new InvalidOperationException($"Static field {fieldReference.FullName} in {method.FullName} is outside the supported primitive mod-field subset.");
            var name = "il_static_" + new string((field.DeclaringType.FullName + "_" + field.Name).Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
            var initialValue = field.HasConstant ? Constant(field.Constant, field.FieldType.FullName) : FindInitializer(method.Module, field);
            if (field.IsPublic && !field.IsInitOnly)
            {
                var configName = new string((field.DeclaringType.Name + "_" + field.Name).Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
                var config = new QuestConfigField(configName, CppType(field.FieldType.FullName), field.Name, initialValue ?? DefaultValue(field.FieldType.FullName));
                ConfigFields[field.FullName] = config;
                StaticFields[field.FullName] = new QuestValue($"getModConfig().{configName}.GetValue()", field.FieldType.FullName, false);
                continue;
            }
            StaticFields[field.FullName] = new QuestValue(name, field.FieldType.FullName, false);
            if (initialValue is not null)
                StaticFieldDeclarations[name] = $"static {CppType(field.FieldType.FullName)} {name} = {initialValue};";
            else
                StaticFieldDeclarations[name] = $"static {CppType(field.FieldType.FullName)} {name}{{}};";
        }
    }

    public MethodDefinition Method { get; }
    public MappingCatalog Catalog { get; }
    public string NativeReturnType { get; }
    public HashSet<string> ActiveLocalMethods { get; }
    public int TemporaryIndex { get; set; }
    public IReadOnlyDictionary<int, QuestValue> Arguments { get; }
    public Dictionary<int, QuestValue> Locals { get; } = new();
    public Dictionary<string, QuestValue> StaticFields { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> StaticFieldDeclarations { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, QuestConfigField> ConfigFields { get; } = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<Instruction, int> InstructionIndexes { get; }
    public Mono.Collections.Generic.Collection<Instruction> Instructions => Method.Body.Instructions;

    private static bool IsPrimitive(string type) => type is "System.Boolean" or "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" or "System.Single" or "System.Double";

    private static string CppType(string type) =>
        type switch
        {
            "System.Boolean" => "bool",
            "System.Byte" => "uint8_t",
            "System.SByte" => "int8_t",
            "System.Int16" => "int16_t",
            "System.UInt16" => "uint16_t",
            "System.Int32" => "int32_t",
            "System.UInt32" => "uint32_t",
            "System.Int64" => "int64_t",
            "System.UInt64" => "uint64_t",
            "System.Single" => "float",
            "System.Double" => "double",
            _ => throw new InvalidOperationException($"Unsupported static field type {type}."),
        };

    private static string Constant(object? value, string type) => type == "System.Boolean" ? ((bool)value! ? "true" : "false") : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)! + (type == "System.Single" ? "f" : "");

    private static string DefaultValue(string type) =>
        type == "System.Boolean" ? "false"
        : type == "System.Single" ? "0.0f"
        : type == "System.Double" ? "0.0"
        : "0";

    private static string? FindInitializer(ModuleDefinition module, FieldDefinition field)
    {
        var initializer = field.DeclaringType.Methods.FirstOrDefault(candidate => candidate.IsConstructor && candidate.IsStatic && candidate.HasBody);
        if (initializer is null)
            return null;
        for (var i = 1; i < initializer.Body.Instructions.Count; i++)
        {
            var instruction = initializer.Body.Instructions[i];
            if (instruction.OpCode.Code != Code.Stsfld || instruction.Operand is not FieldReference reference || reference.Resolve()?.FullName != field.FullName)
                continue;
            var value = initializer.Body.Instructions[i - 1];
            if (value.OpCode.Code == Code.Ldc_I4_0)
                return "0";
            if (value.OpCode.Code == Code.Ldc_I4_1)
                return "1";
            if (value.OpCode.Code == Code.Ldc_I4_2)
                return "2";
            if (value.OpCode.Code == Code.Ldc_I4_3)
                return "3";
            if (value.OpCode.Code == Code.Ldc_I4_4)
                return "4";
            if (value.OpCode.Code == Code.Ldc_I4_5)
                return "5";
            if (value.OpCode.Code == Code.Ldc_I4_6)
                return "6";
            if (value.OpCode.Code == Code.Ldc_I4_7)
                return "7";
            if (value.OpCode.Code == Code.Ldc_I4_8)
                return "8";
            if (value.OpCode.Code is Code.Ldc_I4 or Code.Ldc_I4_S)
                return Convert.ToString(value.Operand, System.Globalization.CultureInfo.InvariantCulture);
            if (value.OpCode.Code == Code.Ldc_R4)
                return Convert.ToString(value.Operand, System.Globalization.CultureInfo.InvariantCulture) + "f";
            if (value.OpCode.Code == Code.Ldc_R8)
                return Convert.ToString(value.Operand, System.Globalization.CultureInfo.InvariantCulture);
        }
        return null;
    }
}
