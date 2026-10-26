using System.Text.Json;
using Mono.Cecil;

namespace Transpiler;

internal sealed record PcAssemblyFingerprint(string Name, Guid Mvid);

internal sealed record CompatibilityProfile(string Id, string PcGameVersion, PcAssemblyFingerprint[] PcAssemblies, string QuestPackageId, string QuestPackageVersion, string QuestApiPackage, string QuestApiVersion);

internal interface IQuestMethodMapping
{
    string QuestType { get; }
    string PublicHeader { get; }
    string DeclarationHeader { get; }
    string QuestMethod { get; }
    string QuestReturn { get; }
    string[] QuestParameters { get; }
    bool QuestStatic { get; }
}

internal sealed record ApiCallMapping(
    string ProfileId,
    string PcAssembly,
    string PcType,
    string PcMethod,
    int PcGenericArity,
    string PcReturn,
    string[] PcParameters,
    bool PcStatic,
    string QuestType,
    string PublicHeader,
    string DeclarationHeader,
    string QuestMethod,
    string QuestReturn,
    string[] QuestParameters,
    bool QuestStatic,
    string? QuestPackageId = null,
    string? QuestPackageVersionRange = null,
    string[]? QuestArgumentConversions = null,
    bool IsQuestLibraryMapping = false,
    string? QuestReturnConversion = null
) : IQuestMethodMapping;

internal sealed record TargetMapping(
    string ProfileId,
    string PcAssembly,
    string PcType,
    string PcMethod,
    int PcGenericArity,
    string PcReturn,
    string[] PcParameters,
    bool PcStatic,
    string QuestType,
    string PublicHeader,
    string DeclarationHeader,
    string QuestMethod,
    string QuestReturn,
    string[] QuestParameters,
    bool QuestStatic,
    string HookName
) : IQuestMethodMapping;

internal sealed class MappingCatalog
{
    private readonly List<ApiCallMapping> _apiCalls;

    private MappingCatalog(CompatibilityProfile profile, List<ApiCallMapping> apiCalls)
    {
        Profile = profile;
        _apiCalls = apiCalls;
    }

    public CompatibilityProfile Profile { get; }
    public IReadOnlyCollection<string> PcAssemblies => _apiCalls.Select(m => m.PcAssembly).Concat(Profile.PcAssemblies.Select(a => a.Name)).Distinct(StringComparer.Ordinal).ToArray();

    public static MappingCatalog Discover(string pcGameVersion, string generatedCandidatesPath, string pcManagedRoot, string templateRoot)
    {
        using var candidates = JsonDocument.Parse(File.ReadAllText(generatedCandidatesPath));
        var discovery = candidates.RootElement.GetProperty("discovery");
        var fingerprints = discovery.GetProperty("pcAssemblyFingerprints").EnumerateArray().Select(item => new PcAssemblyFingerprint(item.GetProperty("name").GetString()!, Guid.Parse(item.GetProperty("mvid").GetString()!))).ToArray();
        if (fingerprints.Length == 0)
            throw new InvalidOperationException("Automatic discovery did not find any PC assemblies to map.");

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(templateRoot, "mod.template.json")));
        var packageId = manifest.RootElement.GetProperty("packageId").GetString()!;
        var packageVersion = manifest.RootElement.GetProperty("packageVersion").GetString()!;
        using var lockDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(templateRoot, "qpm.shared.json")));
        var apiDependencies = lockDocument.RootElement.GetProperty("restoredDependencies").EnumerateArray().Where(item => item.GetProperty("dependency").GetProperty("id").GetString() == "bs-cordl").ToArray();
        if (apiDependencies.Length != 1)
            throw new InvalidOperationException("Automatic compatibility discovery requires exactly one restored bs-cordl dependency in qpm.shared.json.");
        var apiVersion = apiDependencies[0].GetProperty("version").GetString()!;
        var profileId = $"auto-{pcGameVersion}-{apiVersion}";
        var profile = new CompatibilityProfile(profileId, pcGameVersion, fingerprints, packageId, packageVersion, "bs-cordl", apiVersion);
        var apiCalls = GeneratedApiCallCandidateLoader.Load(generatedCandidatesPath, profile, pcManagedRoot).ToList();
        if (apiCalls.Count == 0)
            throw new InvalidOperationException("Automatic PC-to-Quest discovery found no unique exact-signature mappings for the selected game and Quest headers.");
        return new MappingCatalog(profile, apiCalls);
    }

    public bool HasExactPcMapping(MethodDefinition method) => _apiCalls.Any(mapping => Matches(mapping, method));

    public void AddQuestLibraryMappings(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("libraries", out var libraries))
            throw new InvalidOperationException("Quest library mapping catalog must contain a 'libraries' array.");

        foreach (var library in libraries.EnumerateArray())
        {
            var pcAssembly = library.GetProperty("pcAssembly").GetString()!;
            var packageId = library.GetProperty("questPackageId").GetString()!;
            var versionRange = library.GetProperty("questVersionRange").GetString()!;
            foreach (var method in library.GetProperty("methods").EnumerateArray())
            {
                var mapping = new ApiCallMapping(
                    Profile.Id,
                    pcAssembly,
                    method.GetProperty("pcType").GetString()!,
                    method.GetProperty("pcMethod").GetString()!,
                    method.GetProperty("pcGenericArity").GetInt32(),
                    method.GetProperty("pcReturn").GetString()!,
                    method.GetProperty("pcParameters").EnumerateArray().Select(item => item.GetString()!).ToArray(),
                    method.GetProperty("pcStatic").GetBoolean(),
                    method.GetProperty("questType").GetString()!,
                    method.GetProperty("questHeader").GetString()!,
                    method.GetProperty("questHeader").GetString()!,
                    method.GetProperty("questMethod").GetString()!,
                    method.GetProperty("questReturn").GetString()!,
                    method.GetProperty("questParameters").EnumerateArray().Select(item => item.GetString()!).ToArray(),
                    method.GetProperty("questStatic").GetBoolean(),
                    packageId,
                    versionRange,
                    method.TryGetProperty("argumentConversions", out var conversions) ? conversions.EnumerateArray().Select(item => item.GetString()!).ToArray() : Array.Empty<string>(),
                    IsQuestLibraryMapping: true,
                    QuestReturnConversion: method.TryGetProperty("returnConversion", out var returnConversion) ? returnConversion.GetString() : null
                );
                if (
                    _apiCalls.Any(existing =>
                        existing.PcAssembly == mapping.PcAssembly
                        && existing.PcType == mapping.PcType
                        && existing.PcMethod == mapping.PcMethod
                        && existing.PcGenericArity == mapping.PcGenericArity
                        && existing.PcReturn == mapping.PcReturn
                        && existing.PcStatic == mapping.PcStatic
                        && existing.PcParameters.SequenceEqual(mapping.PcParameters)
                    )
                )
                    throw new InvalidOperationException($"Duplicate PC-to-Quest library mapping for {pcAssembly}:{mapping.PcType}.{mapping.PcMethod}.");
                var conversionCount = mapping.QuestArgumentConversions?.Length ?? 0;
                if (mapping.PcParameters.Length != mapping.QuestParameters.Length || conversionCount != 0 && conversionCount != mapping.PcParameters.Length)
                    throw new InvalidOperationException($"Quest library mapping parameter counts do not match for {pcAssembly}:{mapping.PcType}.{mapping.PcMethod}.");
                _apiCalls.Add(mapping);
            }
        }
    }

    public TargetMapping Resolve(MethodDefinition method)
    {
        var apiMapping = _apiCalls.SingleOrDefault(mapping => !mapping.IsQuestLibraryMapping && Matches(mapping, method));
        if (apiMapping is null)
            throw new InvalidOperationException($"No exact mapping for {FormatSignature(method)}.");
        var hookName = string.Concat(
            string.Join("_", new[] { method.Module.Assembly.Name.Name, method.DeclaringType.FullName, method.Name, method.MetadataToken.ToInt32().ToString("x8", System.Globalization.CultureInfo.InvariantCulture) }).Select(character => char.IsLetterOrDigit(character) ? character : '_')
        );
        return new TargetMapping(
            apiMapping.ProfileId,
            apiMapping.PcAssembly,
            apiMapping.PcType,
            apiMapping.PcMethod,
            apiMapping.PcGenericArity,
            apiMapping.PcReturn,
            apiMapping.PcParameters,
            apiMapping.PcStatic,
            apiMapping.QuestType,
            apiMapping.PublicHeader,
            apiMapping.DeclarationHeader,
            apiMapping.QuestMethod,
            apiMapping.QuestReturn,
            apiMapping.QuestParameters,
            apiMapping.QuestStatic,
            hookName
        );
    }

    public ApiCallMapping? ResolveApiCall(MethodReference method)
    {
        MethodDefinition? definition;
        try
        {
            definition = method.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
        catch (ResolutionException)
        {
            return null;
        }
        if (definition is null)
            return null;
        var assembly = definition.Module.Assembly.Name.Name;
        return _apiCalls.SingleOrDefault(mapping =>
            mapping.PcAssembly == assembly
            && mapping.PcType == definition.DeclaringType.FullName
            && mapping.PcMethod == definition.Name
            && mapping.PcGenericArity == definition.GenericParameters.Count
            && mapping.PcReturn == definition.ReturnType.FullName
            && mapping.PcStatic == definition.IsStatic
            && mapping.PcParameters.SequenceEqual(definition.Parameters.Select(parameter => parameter.ParameterType.FullName))
        );
    }

    private static bool Matches(ApiCallMapping mapping, MethodDefinition method) =>
        mapping.PcAssembly == method.Module.Assembly.Name.Name
        && mapping.PcType == method.DeclaringType.FullName
        && mapping.PcMethod == method.Name
        && mapping.PcGenericArity == method.GenericParameters.Count
        && mapping.PcReturn == method.ReturnType.FullName
        && mapping.PcStatic == method.IsStatic
        && mapping.PcParameters.SequenceEqual(method.Parameters.Select(parameter => parameter.ParameterType.FullName));

    private static string FormatSignature(MethodDefinition method) =>
        $"{method.Module.Assembly.Name.Name}:{method.DeclaringType.FullName}.{method.Name}({string.Join(",", method.Parameters.Select(p => p.ParameterType.FullName))}):{method.ReturnType.FullName}:{(method.IsStatic ? "static" : "instance")}";
}
