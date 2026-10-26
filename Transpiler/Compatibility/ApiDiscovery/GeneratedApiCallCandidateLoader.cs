using System.Text.Json;
using Mono.Cecil;

namespace Transpiler;

internal static class GeneratedApiCallCandidateLoader
{
    public static IReadOnlyList<ApiCallMapping> Load(string path, CompatibilityProfile profile, string pcManagedRoot)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Generated API candidate catalog is missing: {path}");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (!root.TryGetProperty("schemaVersion", out var schemaVersion) || schemaVersion.GetInt32() != 2)
            throw new InvalidOperationException("Generated candidate catalog uses an unsupported schema; regenerate it with the current Transpiler.");
        if (!root.TryGetProperty("discovery", out var discovery) || !discovery.TryGetProperty("exactMethodCandidates", out var candidatesElement) || !discovery.TryGetProperty("pcAssemblyFingerprints", out var fingerprintsElement))
            throw new InvalidOperationException("Generated candidate catalog must contain discovery.exactMethodCandidates and discovery.pcAssemblyFingerprints. Regenerate it with the current Transpiler.");

        var fingerprints = fingerprintsElement.EnumerateArray().Select(item => new PcAssemblyFingerprint(item.GetProperty("name").GetString()!, Guid.Parse(item.GetProperty("mvid").GetString()!))).ToArray();
        if (fingerprints.GroupBy(fingerprint => fingerprint.Name, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Generated candidate catalog contains duplicate PC assembly fingerprints.");

        var managedRoot = Path.GetFullPath(pcManagedRoot);
        var gameData = Directory.GetParent(managedRoot)?.FullName;
        var gameRoot = gameData is null ? null : Directory.GetParent(gameData)?.FullName;
        var resolver = new DefaultAssemblyResolver();
        foreach (var directory in new[] { managedRoot, gameRoot is null ? null : Path.Combine(gameRoot, "Libs"), gameRoot is null ? null : Path.Combine(gameRoot, "Plugins") })
            if (directory is not null && Directory.Exists(directory))
                resolver.AddSearchDirectory(directory);
        var sourceAssemblies = new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);

        AssemblyDefinition GetSourceAssembly(string name)
        {
            if (sourceAssemblies.TryGetValue(name, out var cached))
                return cached;
            var fingerprint = fingerprints.SingleOrDefault(item => item.Name == name) ?? throw new InvalidOperationException($"Generated API candidate uses {name}.dll without a PC assembly fingerprint.");
            var sourcePath = QuestTargetProfileValidator.FindPcAssembly(managedRoot, name) ?? throw new InvalidOperationException($"Selected PC install is missing {name}.dll required by generated API candidates.");
            var source = AssemblyDefinition.ReadAssembly(sourcePath, new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false });
            if (source.MainModule.Mvid != fingerprint.Mvid)
            {
                var actualMvid = source.MainModule.Mvid;
                source.Dispose();
                throw new InvalidOperationException($"Generated API candidates were discovered from a different PC {name}.dll (MVID {fingerprint.Mvid}, selected install has {actualMvid}). Regenerate the candidates for this PC install.");
            }
            sourceAssemblies.Add(name, source);
            return source;
        }

        var candidates = candidatesElement.EnumerateArray().ToArray();
        if (candidates.Any(candidate => !candidate.TryGetProperty("resolution", out _) || !candidate.TryGetProperty("matchingQuestDeclarations", out _)))
            throw new InvalidOperationException("Generated API candidates are missing resolution metadata; regenerate them with the current Transpiler.");
        var importable = candidates
            .Where(candidate =>
            {
                if (candidate.GetProperty("resolution").GetString() != "unique-structural-match")
                    return false;
                if (candidate.GetProperty("matchingQuestDeclarations").GetInt32() != 1)
                    throw new InvalidOperationException("Generated API candidate claims a unique match but reports a declaration count other than one.");
                return true;
            })
            .ToArray();
        var resolved = new List<ApiCallMapping>();
        foreach (var candidate in importable)
        {
            var assemblyName = candidate.GetProperty("pcAssembly").GetString()!;
            var pcType = candidate.GetProperty("pcType").GetString()!;
            var pcMethod = candidate.GetProperty("pcMethod").GetString()!;
            var pcReturn = candidate.GetProperty("pcReturn").GetString()!;
            var pcParameters = candidate.GetProperty("pcParameters").EnumerateArray().Select(item => item.GetString()!).ToArray();
            var pcStatic = candidate.GetProperty("pcStatic").GetBoolean();
            var questType = candidate.GetProperty("questType").GetString()!;
            var questMethod = candidate.GetProperty("questMethod").GetString()!;
            var questReturn = candidate.GetProperty("questReturn").GetString()!;
            var questParameters = candidate.GetProperty("questParameters").EnumerateArray().Select(item => item.GetString()!).ToArray();
            var questStatic = candidate.GetProperty("questStatic").GetBoolean();

            var sourceAssembly = GetSourceAssembly(assemblyName);
            var sourceType = CecilTypeWalker.Flatten(sourceAssembly.MainModule.Types).FirstOrDefault(type => type.FullName == pcType);
            if (sourceType is null)
                throw new InvalidOperationException($"Generated API candidate source type {assemblyName}:{pcType} does not exist in the fingerprinted PC assembly.");
            var sourceMethods = sourceType
                .Methods.Where(method =>
                    method.Name == pcMethod
                    && method.GenericParameters.Count == candidate.GetProperty("pcGenericArity").GetInt32()
                    && method.ReturnType.FullName == pcReturn
                    && method.IsStatic == pcStatic
                    && method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(pcParameters, StringComparer.Ordinal)
                )
                .ToArray();
            if (sourceMethods.Length != 1)
                throw new InvalidOperationException($"Generated API candidate source method is missing or ambiguous in the fingerprinted PC assembly: {assemblyName}:{pcType}.{pcMethod}.");
            var sourceMethod = sourceMethods[0];
            var pcCppReturn = QuestApiTypeMapper.Map(sourceMethod.ReturnType);
            var pcCppParameters = sourceMethod.Parameters.Select(parameter => QuestApiTypeMapper.Map(parameter.ParameterType)).ToArray();
            var sourceCppDeclaringType = sourceMethod.DeclaringType.FullName == "System.String" ? "System::String" : QuestApiTypeMapper.Map(sourceMethod.DeclaringType)?.TrimEnd('*').TrimStart(':');
            var declaredPcCppReturn = candidate.GetProperty("pcCppReturn").GetString();
            var declaredPcCppParameters = candidate.GetProperty("pcCppParameters").EnumerateArray().Select(item => item.GetString()).ToArray();
            if (pcCppReturn is null || pcCppParameters.Any(type => type is null) || !string.Equals(declaredPcCppReturn, pcCppReturn, StringComparison.Ordinal) || !declaredPcCppParameters.SequenceEqual(pcCppParameters, StringComparer.Ordinal))
                throw new InvalidOperationException($"Generated API candidate has normalized PC types that do not match its fingerprinted source signature: {assemblyName}:{pcType}.{pcMethod}.");
            if (!QuestApiTypeMapper.QuestReturnMatches(pcCppReturn, questReturn) || !pcCppParameters.SequenceEqual(questParameters, StringComparer.Ordinal) || !string.Equals(sourceCppDeclaringType, questType, StringComparison.Ordinal) || pcStatic != questStatic)
                continue;

            resolved.Add(
                new ApiCallMapping(
                    profile.Id,
                    assemblyName,
                    pcType,
                    pcMethod,
                    candidate.GetProperty("pcGenericArity").GetInt32(),
                    pcReturn,
                    pcParameters,
                    pcStatic,
                    questType,
                    candidate.GetProperty("publicHeader").GetString()!,
                    candidate.GetProperty("declarationHeader").GetString()!,
                    questMethod,
                    questReturn,
                    questParameters,
                    questStatic
                )
            );
        }

        foreach (var group in resolved.GroupBy(CallKey, StringComparer.Ordinal))
        {
            if (group.Count() != 1)
                throw new InvalidOperationException($"Generated candidate catalog has colliding Quest declarations for {group.Key}.");
        }

        var destinationCollisions = resolved.GroupBy(QuestCallKey, StringComparer.Ordinal).Where(group => group.Select(CallKey).Distinct(StringComparer.Ordinal).Count() > 1).ToArray();
        var generatedCollisionKeys = destinationCollisions.Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        resolved.RemoveAll(mapping => generatedCollisionKeys.Contains(QuestCallKey(mapping)));

        foreach (var source in sourceAssemblies.Values)
            source.Dispose();
        return resolved;
    }

    private static string CallKey(ApiCallMapping mapping) => $"{mapping.ProfileId}|{mapping.PcAssembly}|{mapping.PcType}|{mapping.PcMethod}|{mapping.PcGenericArity}|{mapping.PcReturn}|{mapping.PcStatic}|{string.Join(",", mapping.PcParameters)}";

    private static string QuestCallKey(ApiCallMapping mapping) => $"{mapping.QuestType}|{mapping.QuestMethod}|{mapping.QuestReturn}|{mapping.QuestStatic}|{string.Join(",", mapping.QuestParameters)}";
}
