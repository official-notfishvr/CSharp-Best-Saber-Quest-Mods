using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed record ZenjectInstallerLowering(string PluginMethod, string InstallerType, string ServiceType, string QuestEquivalent, string Details);

internal sealed record ZenjectBootstrapOutput(IReadOnlyList<ZenjectInstallerLowering> Installers, IReadOnlyList<string> Unsupported)
{
    public IReadOnlyList<string> StartupComments =>
        Installers
            .SelectMany(installer =>
                new[] { $"// Lowered from Zenjector.Install<{TypeName(installer.InstallerType)}>() and BindInterfacesTo<{TypeName(installer.ServiceType)}>().AsSingle().", $"// {TypeName(installer.ServiceType)} initialization is represented by the Quest late_load bootstrap below." }
            )
            .ToArray();

    private static string TypeName(string fullName) => fullName[(fullName.LastIndexOf('.') + 1)..];
}

internal static class ZenjectBootstrapLowerer
{
    public static ZenjectBootstrapOutput Lower(AssemblyDefinition assembly, EmbeddedAssetBundleOutput embeddedAssets)
    {
        var types = CecilTypeWalker.Flatten(assembly.MainModule.Types).ToArray();
        var installers = new List<ZenjectInstallerLowering>();
        var unsupported = new List<string>();

        foreach (var method in types.SelectMany(type => type.Methods).Where(method => method.HasBody))
        foreach (var instruction in method.Body.Instructions.Where(instruction => instruction.OpCode.Code is Code.Call or Code.Callvirt))
        {
            if (instruction.Operand is not GenericInstanceMethod call || call.Name != "Install" || !call.DeclaringType.Name.Contains("Zenjector", StringComparison.Ordinal))
                continue;

            if (call.GenericArguments.Count != 1 || call.GenericArguments[0].Resolve() is not { } installerType)
            {
                unsupported.Add($"{method.FullName}: Zenjector.Install must use a resolvable installer type.");
                continue;
            }

            var installBindings = installerType.Methods.FirstOrDefault(candidate => candidate.Name == "InstallBindings" && !candidate.IsStatic && candidate.HasBody && candidate.Parameters.Count == 0);
            if (installBindings is null)
            {
                unsupported.Add($"{method.FullName}: installer {installerType.FullName} has no supported instance InstallBindings() body.");
                continue;
            }

            var binding = installBindings.Body.Instructions.Where(candidate => candidate.Operand is GenericInstanceMethod generic && generic.Name == "BindInterfacesTo" && generic.GenericArguments.Count == 1).Select(candidate => (GenericInstanceMethod)candidate.Operand).FirstOrDefault();
            var hasSingleScope = installBindings.Body.Instructions.Any(candidate => candidate.Operand is MethodReference called && called.Name == "AsSingle");
            if (binding is null || !hasSingleScope || binding.GenericArguments[0].Resolve() is not { } serviceType)
            {
                unsupported.Add($"{method.FullName}: installer {installerType.FullName} must bind one resolvable service through BindInterfacesTo<T>().AsSingle().");
                continue;
            }

            var initializable = serviceType.Interfaces.Any(iface => iface.InterfaceType.FullName == "Zenject.IInitializable");
            var initialize = serviceType.Methods.FirstOrDefault(candidate => candidate.Name == "Initialize" && !candidate.IsStatic && candidate.HasBody && candidate.Parameters.Count == 0);
            var startsCoroutine = initialize?.Body.Instructions.Any(candidate => candidate.Operand is MethodReference called && called.Name == "StartCoroutine") == true;
            if (!initializable || initialize is null || !startsCoroutine)
            {
                unsupported.Add($"{method.FullName}: service {serviceType.FullName} must implement Zenject.IInitializable and start its supported coroutine from Initialize().");
                continue;
            }

            var serviceStateMachines = types
                .Where(candidate => candidate.DeclaringType?.FullName == serviceType.FullName || candidate.FullName.StartsWith(serviceType.FullName + "/", StringComparison.Ordinal))
                .SelectMany(candidate => candidate.Methods)
                .Where(candidate => candidate.HasBody)
                .ToArray();
            var resourceNames = serviceStateMachines.SelectMany(candidate => candidate.Body.Instructions).Where(candidate => candidate.OpCode.Code == Code.Ldstr && candidate.Operand is string).Select(candidate => (string)candidate.Operand).ToHashSet(StringComparer.Ordinal);
            var matchingAssets = embeddedAssets.Bundles.Where(bundle => resourceNames.Contains(bundle.ResourceName)).ToArray();
            if (matchingAssets.Length == 0)
            {
                unsupported.Add($"{method.FullName}: service {serviceType.FullName} starts a coroutine, but no referenced embedded asset bundle could be mapped to it.");
                continue;
            }

            installers.Add(
                new(
                    method.FullName,
                    installerType.FullName,
                    serviceType.FullName,
                    "Scotland2 late_load asset bootstrap",
                    $"Lowered Zenjector.Install<{installerType.Name}> and BindInterfacesTo<{serviceType.Name}>().AsSingle(); the IInitializable coroutine's embedded bundle asset assignment is emitted in late_load."
                )
            );
        }

        return new(installers, unsupported);
    }
}
