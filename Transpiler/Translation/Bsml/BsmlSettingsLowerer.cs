using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal static class BsmlSettingsLowerer
{
    private static readonly IReadOnlyDictionary<string, HashSet<string>> SettingValueTypes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
    {
        ["toggle-setting"] = Set("System.Boolean"),
        ["slider-setting"] = Set("System.Single", "System.Int32"),
        ["increment-setting"] = Set("System.Single", "System.Int32"),
        ["dropdown-list-setting"] = Set("System.String"),
        ["list-setting"] = Set("System.String"),
        ["list-slider-setting"] = Set("System.String"),
        ["color-setting"] = Set("UnityEngine.Color"),
        ["string-setting"] = Set("System.String"),
    };

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);

    public static BsmlLoweringResult Lower(AssemblyDefinition assembly, string modId, MappingCatalog catalog)
    {
        var menus = new List<Menu>();
        var unsupported = new List<string>();
        foreach (var type in CecilTypeWalker.Flatten(assembly.MainModule.Types))
        foreach (var method in type.Methods.Where(m => m.HasBody))
        {
            var instructions = method.Body.Instructions;
            for (var i = 0; i < instructions.Count; i++)
            {
                if (instructions[i].Operand is not MethodReference call || call.Name != "AddSettingsMenu" || !call.DeclaringType.FullName.EndsWith("BSMLSettings", StringComparison.Ordinal))
                    continue;
                try
                {
                    var arguments = GetRegistrationArguments(method, i);
                    var resource = assembly.MainModule.Resources.OfType<EmbeddedResource>().SingleOrDefault(r => r.Name == arguments.Resource) ?? throw Error(method, instructions[i], $"embedded resource '{arguments.Resource}' was not found");
                    using var stream = resource.GetResourceStream();
                    using var reader = new StreamReader(stream);
                    var markup = reader.ReadToEnd();
                    var parsed = ParseMarkup(markup, resource.Name);
                    Bindings? bindings = null;
                    GenericBindings? genericBindings = null;
                    if (CanLowerIndexedConfigBinding(arguments.Host, parsed))
                        bindings = LowerBindings(arguments.Host, parsed, resource.Name);
                    else
                        genericBindings = LowerGenericBindings(arguments.Host, parsed, resource.Name, catalog);
                    var key = $"{modId}_{Sanitize(resource.Name)}";
                    menus.Add(new Menu(arguments.Title, resource.Name, bindings?.Markup ?? genericBindings!.Markup, key, Sanitize(resource.Name), bindings, genericBindings));
                }
                catch (InvalidOperationException ex)
                {
                    unsupported.Add(ex.Message);
                }
            }
        }
        return new BsmlLoweringResult(menus, unsupported);
    }

    private static (string Title, string Resource, TypeDefinition Host) GetRegistrationArguments(MethodDefinition method, int callIndex)
    {
        var ins = method.Body.Instructions;
        var found = new List<(string Value, int Index)>();
        for (var n = callIndex - 1; n >= 0 && callIndex - n <= 12; n--)
            if (ins[n].OpCode.Code == Code.Ldstr && ins[n].Operand is string s)
                found.Add((s, n));
        found.Reverse();
        var resource = found.LastOrDefault();
        var title = found.Count > 1 ? found[^2] : default;
        if (string.IsNullOrEmpty(title.Value) || string.IsNullOrEmpty(resource.Value))
            throw Error(method, ins[callIndex], "could not resolve constant title and resource arguments from registration stack");
        TypeDefinition? host = null;
        for (var n = resource.Index + 1; n < callIndex; n++)
        {
            if (ins[n].OpCode.Code == Code.Ldsfld && ins[n].Operand is FieldReference field)
                host = ResolveDefinition(field.FieldType);
            if ((ins[n].OpCode.Code is Code.Call or Code.Callvirt) && ins[n].Operand is MethodReference getter && getter.Name.StartsWith("get_", StringComparison.Ordinal))
                host = ResolveDefinition(getter.ReturnType);
            if (ins[n].OpCode.Code == Code.Newobj && ins[n].Operand is MethodReference ctor)
                host = ResolveDefinition(ctor.DeclaringType);
        }
        if (host is null)
            throw Error(method, ins[callIndex], "could not resolve the settings host type from the registration arguments");
        return (title.Value, resource.Value, host);
    }

    private static XElement ParseMarkup(string markup, string resource)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse("<root>" + markup + "</root>", LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"BSML resource '{resource}': invalid markup: {ex.Message}");
        }
        return doc.Root!;
    }

    private static Bindings LowerBindings(TypeDefinition host, XElement root, string resource)
    {
        var values = new Dictionary<string, (FieldDefinition? Field, PropertyDefinition? Property)>(StringComparer.Ordinal);
        foreach (var field in host.Fields)
        foreach (var attr in field.CustomAttributes.Where(IsUiValue))
            values[AttributeString(attr, host, field.Name)] = (field, null);
        foreach (var property in host.Properties)
        foreach (var attr in property.CustomAttributes.Where(IsUiValue))
            values[AttributeString(attr, host, property.Name)] = (null, property);

        var dropdowns = root.Descendants("dropdown-list-setting").ToArray();
        if (dropdowns.Length != 1)
            throw new InvalidOperationException($"BSML resource '{resource}': supported settings host requires exactly one dropdown-list-setting (found {dropdowns.Length}).");
        var dropdown = dropdowns[0];
        var valueKey = RequiredAttr(dropdown, "value", resource);
        var choicesKey = RequiredAttr(dropdown, "choices", resource);
        if (!values.TryGetValue(valueKey, out var valueMember) || valueMember.Property is not PropertyDefinition valueProperty)
        {
            var discoveredAttributes = host
                .Fields.SelectMany(f => f.CustomAttributes.Select(a => $"{f.Name}:{a.AttributeType.FullName}@{a.AttributeType.Scope.Name}"))
                .Concat(host.Properties.SelectMany(p => p.CustomAttributes.Select(a => $"{p.Name}:{a.AttributeType.FullName}@{a.AttributeType.Scope.Name}")));
            throw new InvalidOperationException($"BSML resource '{resource}': bind-value '{valueKey}' must resolve to a [UIValue] string property on host '{host.FullName}'. Discovered keys: {string.Join(", ", values.Keys)}. Attributes: {string.Join("; ", discoveredAttributes)}.");
        }
        if (!values.TryGetValue(choicesKey, out var choicesMember) || choicesMember.Field is not FieldDefinition choicesField)
            throw new InvalidOperationException($"BSML resource '{resource}': choices '{choicesKey}' must resolve to a [UIValue] list field.");
        var options = ReadStringListInitializer(host, choicesField);
        var setter = LowerStringIndexBinding(valueProperty, resource);
        var config = FindConfigWrite(setter, resource);
        var rewrittenValue = BindingIdentifier(valueKey);
        var rewrittenChoices = BindingIdentifier(choicesKey);
        dropdown.SetAttributeValue("value", rewrittenValue);
        dropdown.SetAttributeValue("choices", rewrittenChoices);
        var markup = string.Concat(root.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting)));
        return new Bindings(rewrittenValue, rewrittenChoices, options, config, markup);
    }

    private static bool IsUiValue(CustomAttribute attribute) => attribute.AttributeType.FullName == "BeatSaberMarkupLanguage.Attributes.UIValue" && attribute.AttributeType.Scope.Name == "BSML";

    private static bool CanLowerIndexedConfigBinding(TypeDefinition host, XElement root)
    {
        var dropdowns = root.Descendants("dropdown-list-setting").ToArray();
        if (dropdowns.Length != 1)
            return false;
        var valueKey = (string?)dropdowns[0].Attribute("value");
        var choicesKey = (string?)dropdowns[0].Attribute("choices");
        if (valueKey is null || choicesKey is null)
            return false;

        var valueProperties = host.Properties.Where(property => property.CustomAttributes.Where(IsUiValue).Any(attribute => AttributeString(attribute, host, property.Name) == valueKey)).ToArray();
        var choicesFields = host.Fields.Where(field => field.CustomAttributes.Where(IsUiValue).Any(attribute => AttributeString(attribute, host, field.Name) == choicesKey)).ToArray();
        if (valueProperties.Length != 1 || choicesFields.Length != 1)
            return false;
        var valueProperty = valueProperties[0];
        var choicesField = choicesFields[0];
        if (valueProperty?.GetMethod is not { HasBody: true } getter || valueProperty.SetMethod is not { HasBody: true } setter || choicesField is null)
            return false;
        var getCalls = getter.Body.Instructions.Where(i => i.Operand is MethodReference).Select(i => (MethodReference)i.Operand).ToArray();
        var setCalls = setter.Body.Instructions.Where(i => i.Operand is MethodReference).Select(i => (MethodReference)i.Operand).ToArray();
        return getCalls.Any(call => call.Name == "get_Item" && call.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal))
            && setCalls.Any(call => call.Name == "IndexOf" && call.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal))
            && setter.Body.Instructions.Any(i => i.OpCode.Code == Code.Stsfld && i.Operand is FieldReference field && field.FieldType.FullName == "System.Int32");
    }

    private static GenericBindings LowerGenericBindings(TypeDefinition host, XElement root, string resource, MappingCatalog catalog)
    {
        var values = new Dictionary<string, (FieldDefinition? Field, PropertyDefinition? Property)>(StringComparer.Ordinal);
        foreach (var field in host.Fields)
        foreach (var attr in field.CustomAttributes.Where(IsUiValue))
            AddValue(values, AttributeString(attr, host, field.Name), (field, null), resource);
        foreach (var property in host.Properties)
        foreach (var attr in property.CustomAttributes.Where(IsUiValue))
            AddValue(values, AttributeString(attr, host, property.Name), (null, property), resource);

        var actions = new Dictionary<string, GenericAction>(StringComparer.Ordinal);
        var referencedActions = root.Descendants().SelectMany(element => element.Attributes()).Where(attribute => attribute.Name.LocalName.StartsWith("on-", StringComparison.Ordinal)).Select(attribute => attribute.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var method in host.Methods)
        {
            var uiAction = method.CustomAttributes.FirstOrDefault(IsUiAction);
            var key = uiAction is null ? method.Name : AttributeString(uiAction, host, method.Name);
            if (!referencedActions.Contains(key))
                continue;
            if (method.IsStatic || method.Parameters.Count != 0 || method.ReturnType.FullName != "System.Void")
                throw new InvalidOperationException($"BSML resource '{resource}': action '{key}' must be an instance void method with no parameters.");
            var loweredAction = IlMethodTranslator.TranslateBsmlAction(method, catalog);
            actions.Add(
                key,
                new GenericAction(
                    BindingIdentifier("action_" + key),
                    IlControlFlowLowerer.RenderBsmlAction(loweredAction),
                    loweredAction.Select(statement => statement.ApiMapping).OfType<ApiCallMapping>().DistinctBy(mapping => mapping.PublicHeader + "|" + mapping.QuestMethod).ToArray(),
                    loweredAction.Select(statement => statement.ConfigField).OfType<QuestConfigField>().DistinctBy(field => field.Name).ToArray()
                )
            );
        }

        var loweredValues = new Dictionary<string, GenericValue>(StringComparer.Ordinal);
        foreach (var (key, member) in values)
        {
            if (!root.Descendants().SelectMany(element => element.Attributes()).Any(attribute => attribute.Value == key))
                continue;
            var type = member.Field?.FieldType ?? member.Property!.PropertyType;
            if (type is GenericInstanceType listType && listType.ElementType.FullName.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal))
            {
                var choicesField = member.Field ?? FindBackingField(host, member.Property!);
                if (choicesField is null || listType.GenericArguments.Count != 1 || listType.GenericArguments[0].FullName is not ("System.Object" or "System.String"))
                    throw new InvalidOperationException($"BSML resource '{resource}': choices value '{key}' must be an initialized List<object> or List<string> field.");
                var options = ReadStringListInitializer(host, choicesField);
                loweredValues.Add(key, new GenericValue(BindingIdentifier(key), type.FullName, "System::Collections::Generic::List_1<System::Object*>*", "", true, options));
                continue;
            }

            var (cppType, defaultValue) = LowerValueType(host, key, member.Field, member.Property, type, resource);
            loweredValues.Add(key, new GenericValue(BindingIdentifier(key), type.FullName, cppType, defaultValue, false, Array.Empty<string>()));
        }

        foreach (var element in root.Descendants())
        {
            foreach (var name in new[] { "value", "choices", "bind-value" })
            {
                var attr = element.Attribute(name);
                if (attr is null)
                    continue;
                if (!loweredValues.TryGetValue(attr.Value, out var value))
                    continue;
                if (name == "choices" && !value.IsChoices)
                    throw new InvalidOperationException($"BSML resource '{resource}': choices binding '{attr.Value}' must be a supported List<object> or List<string> value.");
                if (name == "value" && SettingValueTypes.TryGetValue(element.Name.LocalName, out var expected))
                {
                    var allowed = element.Name.LocalName == "increment-setting" && (string?)element.Attribute("integer-only") == "true" ? Set("System.Int32") : expected;
                    if (!allowed.Contains(value.SourceType))
                        throw new InvalidOperationException($"BSML resource '{resource}': <{element.Name}> value '{attr.Value}' has type '{value.SourceType}', expected {string.Join(" or ", allowed)}.");
                }
                element.SetAttributeValue(name, value.GeneratedName);
            }
            foreach (var actionAttribute in element.Attributes().Where(attribute => attribute.Name.LocalName.StartsWith("on-", StringComparison.Ordinal)))
            {
                if (actions.TryGetValue(actionAttribute.Value, out var action))
                    actionAttribute.Value = action.GeneratedName;
            }
        }

        var markup = string.Concat(root.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting)));
        return new GenericBindings(loweredValues.Values.ToArray(), actions.Values.ToArray(), markup);
    }

    private static void AddValue(Dictionary<string, (FieldDefinition? Field, PropertyDefinition? Property)> values, string key, (FieldDefinition? Field, PropertyDefinition? Property) member, string resource)
    {
        if (!values.TryAdd(key, member))
            throw new InvalidOperationException($"BSML resource '{resource}': duplicate [UIValue] key '{key}'.");
    }

    private static FieldDefinition? FindBackingField(TypeDefinition host, PropertyDefinition property) => host.Fields.SingleOrDefault(field => field.Name == $"<{property.Name}>k__BackingField");

    private static bool IsUiAction(CustomAttribute attribute) => attribute.AttributeType.FullName == "BeatSaberMarkupLanguage.Attributes.UIAction" && attribute.AttributeType.Scope.Name == "BSML";

    private static (string CppType, string Initial) LowerValueType(TypeDefinition host, string key, FieldDefinition? field, PropertyDefinition? property, TypeReference type, string resource)
    {
        var mapped = type.FullName switch
        {
            "System.Boolean" => ("bool", "false"),
            "System.Int32" => ("int32_t", "0"),
            "System.Single" => ("float", "0.0f"),
            "System.String" => ("::StringW", "il2cpp_utils::newcsstr(\"\")"),
            "UnityEngine.Color" => ("UnityEngine::Color", "UnityEngine::Color(0.0f, 0.0f, 0.0f, 0.0f)"),
            _ => throw new InvalidOperationException($"BSML resource '{resource}': [UIValue] '{key}' has unsupported host type '{type.FullName}'."),
        };

        if (property is not null && property.GetMethod is not null && property.SetMethod is not null)
            return (mapped.Item1, ReadPropertyInitializer(host, property, mapped.Item1, mapped.Item2));
        if (field is not null && !field.IsInitOnly)
            return (mapped.Item1, ReadFieldInitializer(host, field, mapped.Item1, mapped.Item2));
        throw new InvalidOperationException($"BSML resource '{resource}': [UIValue] '{key}' must be a writable property or mutable field.");
    }

    private static string ReadPropertyInitializer(TypeDefinition host, PropertyDefinition property, string cppType, string fallback)
    {
        if (FindBackingField(host, property) is { } backingField)
        {
            var fromBackingField = ReadFieldInitializer(host, backingField, cppType, fallback);
            if (fromBackingField != fallback)
                return fromBackingField;
        }
        foreach (var ctor in host.Methods.Where(m => m.IsConstructor && !m.IsStatic && m.HasBody))
        {
            var instructions = ctor.Body.Instructions;
            for (var index = 0; index < instructions.Count; index++)
                if (instructions[index].Operand is MethodReference call && call.Name == "set_" + property.Name)
                {
                    var constants = instructions.Take(index).TakeLast(8).Where(IsConstantInstruction).ToArray();
                    return FormatInitializer(constants, cppType, fallback);
                }
        }
        return fallback;
    }

    private static string ReadFieldInitializer(TypeDefinition host, FieldDefinition field, string cppType, string fallback)
    {
        foreach (var ctor in host.Methods.Where(m => m.IsConstructor && !m.IsStatic && m.HasBody))
        {
            var instructions = ctor.Body.Instructions;
            for (var index = 0; index < instructions.Count; index++)
                if (instructions[index].OpCode.Code == Code.Stfld && instructions[index].Operand is FieldReference target && target.FullName == field.FullName)
                    return FormatInitializer(instructions.Take(index).TakeLast(8).Where(IsConstantInstruction).ToArray(), cppType, fallback);
        }
        return fallback;
    }

    private static bool IsConstantInstruction(Instruction instruction) =>
        instruction.OpCode.Code is Code.Ldstr or Code.Ldc_I4 or Code.Ldc_I4_S or Code.Ldc_I4_0 or Code.Ldc_I4_1 or Code.Ldc_I4_2 or Code.Ldc_I4_3 or Code.Ldc_I4_4 or Code.Ldc_I4_5 or Code.Ldc_I4_6 or Code.Ldc_I4_7 or Code.Ldc_I4_8 or Code.Ldc_I4_M1 or Code.Ldc_R4;

    private static string FormatInitializer(Instruction[] values, string cppType, string fallback)
    {
        if (cppType == "UnityEngine::Color")
        {
            var floats = values.Where(i => i.OpCode.Code == Code.Ldc_R4).Select(i => Convert.ToSingle(i.Operand, CultureInfo.InvariantCulture)).ToArray();
            return floats.Length >= 4 ? $"UnityEngine::Color({Float(floats[^4])}, {Float(floats[^3])}, {Float(floats[^2])}, {Float(floats[^1])})" : fallback;
        }
        var value = values.LastOrDefault();
        if (value is null)
            return fallback;
        if (cppType == "::StringW" && value.OpCode.Code == Code.Ldstr)
            return $"il2cpp_utils::newcsstr(\"{EscapeCpp((string)value.Operand)}\")";
        if (cppType == "float" && value.OpCode.Code == Code.Ldc_R4)
            return Float(Convert.ToSingle(value.Operand, CultureInfo.InvariantCulture));
        if (cppType == "bool" && TryInt(value, out var boolean))
            return boolean == 0 ? "false" : "true";
        if (cppType == "int32_t" && TryInt(value, out var integer))
            return integer.ToString(CultureInfo.InvariantCulture);
        return fallback;
    }

    private static string Float(float value) => value.ToString("0.0########", CultureInfo.InvariantCulture) + "f";

    private static string AttributeString(CustomAttribute attr, TypeDefinition host, string member) =>
        attr.ConstructorArguments.Count == 1 && attr.ConstructorArguments[0].Value is string value ? value : throw new InvalidOperationException($"BSML host {host.FullName}.{member}: [UIValue] key must be a single constant string.");

    private static string[] ReadStringListInitializer(TypeDefinition host, FieldDefinition field)
    {
        var storeCode = field.IsStatic ? Code.Stsfld : Code.Stfld;
        var initializer =
            host.Methods.Where(m => m.HasBody && (field.IsStatic ? m.Name == ".cctor" : m.IsConstructor && !m.IsStatic)).FirstOrDefault(m => m.Body.Instructions.Any(i => i.OpCode.Code == storeCode && i.Operand is FieldReference target && target.FullName == field.FullName))
            ?? throw new InvalidOperationException($"BSML host {host.FullName}: choices field '{field.Name}' has no supported initializer.");
        var instructions = initializer.Body.Instructions;
        var targetIndex = instructions.ToList().FindIndex(i => i.OpCode.Code == storeCode && i.Operand is FieldReference target && target.FullName == field.FullName);
        if (targetIndex < 0)
            throw new InvalidOperationException($"BSML host {host.FullName}: choices field '{field.Name}' is not assigned by its initializer.");
        var segmentStart = targetIndex - 1;
        while (segmentStart >= 0 && instructions[segmentStart].OpCode.Code is not (Code.Stsfld or Code.Stfld))
            segmentStart--;
        segmentStart++;
        var list = new List<string>();
        var foundList = false;
        for (var index = segmentStart; index < targetIndex; index++)
        {
            var instruction = instructions[index];
            if (instruction.OpCode.Code == Code.Ldstr && instruction.Operand is string text)
                list.Add(text);
            else if (instruction.OpCode.Code == Code.Newobj && instruction.Operand is MethodReference ctor && ctor.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal))
                foundList = true;
            else if (instruction.OpCode.Code == Code.Callvirt && instruction.Operand is MethodReference call && call.Name == "Add" && call.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal)) { }
            else if (instruction.OpCode.Code is Code.Dup or Code.Pop or Code.Nop or Code.Ldarg_0) { }
            else if (instruction.OpCode.Code == Code.Call && instruction.Operand is MethodReference baseCtor && baseCtor.Name == ".ctor" && baseCtor.DeclaringType.FullName == "System.Object") { }
            else
                throw Error(initializer, instruction, $"choices initializer for member '{field.Name}' contains unsupported IL");
        }
        if (!foundList || list.Count == 0)
            throw new InvalidOperationException($"BSML host {host.FullName}: choices field '{field.Name}' must initialize from a list of constant strings.");
        return list.ToArray();
    }

    private static MethodDefinition LowerStringIndexBinding(PropertyDefinition property, string resource)
    {
        var getter = property.GetMethod ?? throw new InvalidOperationException($"BSML resource '{resource}': property '{property.Name}' has no getter.");
        var setter = property.SetMethod ?? throw new InvalidOperationException($"BSML resource '{resource}': property '{property.Name}' has no setter.");
        var getCodes = getter.Body.Instructions.Select(i => i.OpCode.Code).ToArray();
        if (
            !getCodes.Contains(Code.Ldsfld)
            || !getCodes.Contains(Code.Ldarg_0)
            || !getCodes.Contains(Code.Callvirt)
            || !getter.Body.Instructions.Any(i => i.Operand is MethodReference called && called.Name == "get_Item" && called.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal))
            || !getCodes.Contains(Code.Castclass)
        )
            throw Error(getter, getter.Body.Instructions.Last(), "getter must load the choices list, read an integer index, index the list, and cast to string");
        var setCalls = setter.Body.Instructions.Where(i => i.OpCode.Code is Code.Call or Code.Callvirt).Select(i => i.Operand as MethodReference).OfType<MethodReference>().ToArray();
        if (!setCalls.Any(c => c.Name == "IndexOf" && c.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal)))
            throw Error(setter, setter.Body.Instructions.Last(), "setter must derive the selected index through List<T>.IndexOf");
        if (!setter.Body.Instructions.Any(i => i.OpCode.Code == Code.Stsfld))
            throw Error(setter, setter.Body.Instructions.Last(), "setter must assign the derived index to a static integer setting field");
        return setter;
    }

    private static string FindConfigWrite(MethodDefinition setter, string resource)
    {
        var targets = setter.Body.Instructions.Where(i => i.OpCode.Code == Code.Stsfld).Select(i => i.Operand as FieldReference).OfType<FieldReference>().DistinctBy(f => f.FullName).ToArray();
        if (targets.Length != 1 || targets[0].FieldType.FullName != "System.Int32")
            throw new InvalidOperationException($"BSML resource '{resource}', setter {setter.FullName}: expected one static Int32 setting write; found {targets.Length}.");
        var field = ResolveDefinition(targets[0].DeclaringType)?.Fields.SingleOrDefault(f => f.FullName == targets[0].FullName) ?? throw new InvalidOperationException($"BSML resource '{resource}', setter {setter.FullName}: cannot resolve config-backed field {targets[0].FullName}.");
        var initial = "0";
        var cctor = field.DeclaringType.Methods.SingleOrDefault(m => m.Name == ".cctor" && m.HasBody);
        var instructions = cctor?.Body.Instructions;
        if (instructions is not null)
            for (var i = 0; i + 1 < instructions.Count; i++)
                if (instructions[i + 1].OpCode.Code == Code.Stsfld && instructions[i + 1].Operand is FieldReference stored && stored.FullName == field.FullName && TryInt(instructions[i], out var value))
                    initial = value.ToString(CultureInfo.InvariantCulture);
        return $"{Sanitize(field.DeclaringType.Name)}_{Sanitize(field.Name)}|{field.Name}|{initial}";
    }

    private static string BindingIdentifier(string key) => "bsml_" + Sanitize(key).Trim('_');

    private static string RequiredAttr(XElement e, string name, string resource) => (string?)e.Attribute(name) ?? throw new InvalidOperationException($"BSML resource '{resource}': <{e.Name}> requires '{name}'.");

    private static bool TryInt(Instruction instruction, out int value)
    {
        value = instruction.OpCode.Code switch
        {
            Code.Ldc_I4_M1 => -1,
            Code.Ldc_I4_0 => 0,
            Code.Ldc_I4_1 => 1,
            Code.Ldc_I4_2 => 2,
            Code.Ldc_I4_3 => 3,
            Code.Ldc_I4_4 => 4,
            Code.Ldc_I4_5 => 5,
            Code.Ldc_I4_6 => 6,
            Code.Ldc_I4_7 => 7,
            Code.Ldc_I4_8 => 8,
            Code.Ldc_I4 or Code.Ldc_I4_S => Convert.ToInt32(instruction.Operand, CultureInfo.InvariantCulture),
            _ => int.MinValue,
        };
        return value != int.MinValue;
    }

    private static TypeDefinition? ResolveDefinition(TypeReference type)
    {
        try
        {
            return type.Resolve();
        }
        catch
        {
            return null;
        }
    }

    private static InvalidOperationException Error(MethodDefinition method, Instruction instruction, string detail) => new($"BSML method {method.FullName} at IL_{instruction.Offset:x4}: {detail}.");

    private static string Sanitize(string value) => System.Text.RegularExpressions.Regex.Replace(value, "[^A-Za-z0-9_]", "_");

    private static string EscapeCpp(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    internal sealed record Bindings(string ValueMember, string ChoicesMember, string[] Options, string ConfigInfo, string Markup);

    internal sealed record GenericValue(string GeneratedName, string SourceType, string CppType, string InitialValue, bool IsChoices, string[] Options);

    internal sealed record GenericAction(string GeneratedName, string Body, ApiCallMapping[] ApiMappings, QuestConfigField[] ConfigFields);

    internal sealed record GenericBindings(GenericValue[] Values, GenericAction[] Actions, string Markup);

    internal sealed record Menu(string Title, string ResourceName, string Markup, string DataKey, string DataToken, Bindings? Bindings, GenericBindings? GenericBindings);
}

internal sealed record BsmlLoweringResult(IReadOnlyList<BsmlSettingsLowerer.Menu> Menus, IReadOnlyList<string> Unsupported);
