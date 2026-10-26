using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed record LifecycleOperation(string PcMethod, string PcApi, string QuestEquivalent, string Status, string Details, IReadOnlyList<string>? PcCalls = null);

internal sealed record PluginLifecycleOutput(bool NeedsUnload, IReadOnlyList<LifecycleOperation> Operations);

internal static class PluginLifecycleLowerer
{
    public static PluginLifecycleOutput Lower(AssemblyDefinition assembly)
    {
        var methods = CecilTypeWalker.Flatten(assembly.MainModule.Types).SelectMany(type => type.Methods).ToArray();
        var starts = methods.Where(method => HasIpaLifecycleAttribute(method, "OnStartAttribute")).ToArray();
        var exits = methods.Where(method => HasIpaLifecycleAttribute(method, "OnExitAttribute")).ToArray();
        var operations = new List<LifecycleOperation>();

        foreach (var method in starts)
            operations.Add(new(method.FullName, "IPA.OnStartAttribute", "Scotland2 late_load()", "timing_mapped_body_not_lowered", "The lifecycle timing maps to late_load; this method's IL body is not emitted.", GetCalls(method)));
        foreach (var method in exits)
            operations.Add(new(method.FullName, "IPA.OnExitAttribute", "Scotland2 unload()", "timing_mapped_body_not_lowered", "The lifecycle timing maps to unload; this method's IL body is not emitted.", GetCalls(method)));

        var eventUses = methods
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions.Where(instruction => (instruction.OpCode.Code is Code.Call or Code.Callvirt) && instruction.Operand is MethodReference).Select(instruction => (Owner: method, Call: (MethodReference)instruction.Operand)))
            .Where(item => item.Call.Name.Contains("MainMenuInitializing", StringComparison.Ordinal) && item.Call.DeclaringType.FullName.Contains("MainMenuAwaiter", StringComparison.Ordinal))
            .ToArray();
        foreach (var item in eventUses)
            operations.Add(new(item.Owner.FullName, item.Call.FullName, "BSML::Register::RegisterSettingsMenu from late_load()", "unsupported", "Quest BSML has no MainMenuAwaiter.MainMenuInitializing event; menu registration is performed at late_load, the supported Quest registration point."));

        return new PluginLifecycleOutput(exits.Length > 0, operations);
    }

    private static bool HasIpaLifecycleAttribute(MethodDefinition method, string name) => method.CustomAttributes.Any(attribute => attribute.AttributeType.Name == name && attribute.AttributeType.Namespace.StartsWith("IPA", StringComparison.Ordinal));

    private static string[] GetCalls(MethodDefinition method) =>
        !method.HasBody ? Array.Empty<string>() : method.Body.Instructions.Where(instruction => instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj).Select(instruction => instruction.Operand is MethodReference called ? called.FullName : instruction.ToString()).ToArray();
}
