using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace Transpiler;

internal static partial class QuestLibraryMappingGenerator
{
    private const int MinimumNameScore = 45;
    private const int RequiredWinnerMargin = 12;

    public static string Generate(string pcManagedRoot, string qpmCacheRoot, IEnumerable<string> referencedAssemblies, AssemblyDefinition? sourceMod = null, string? generatedCandidatesPath = null)
    {
        var libraries = new JsonArray();
        var assemblyAssociations = new JsonArray();
        var typeAssociations = new JsonArray();
        var dependencyAdapters = new JsonArray();
        var unresolvedCalls = new JsonArray();
        if (!Directory.Exists(qpmCacheRoot))
            return Catalog(libraries, assemblyAssociations, typeAssociations, dependencyAdapters, unresolvedCalls);

        var packages = DiscoverPackages(qpmCacheRoot);
        var references = referencedAssemblies.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var consumedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assemblyName in referencedAssemblies.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var assemblyPath = QuestTargetProfileValidator.FindPcAssembly(pcManagedRoot, assemblyName);
            if (assemblyPath is null)
                continue;
            var package = packages.FirstOrDefault(candidate => PackageMatches(assemblyName, candidate.Id));
            if (package is null)
                continue;
            consumedPackageIds.Add(package.Id);

            using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { ReadSymbols = false });
            var pcMethods = CecilTypeWalker
                .Flatten(assembly.MainModule.Types)
                .Where(type => type.IsPublic || type.IsNestedPublic)
                .SelectMany(type => type.Methods.Where(method => method.IsPublic && !method.IsConstructor))
                .OrderBy(method => method.DeclaringType.FullName, StringComparer.Ordinal)
                .ThenBy(method => method.Name, StringComparer.Ordinal)
                .ThenBy(method => method.MetadataToken.ToInt32())
                .ToArray();
            var questMethods = ReadQuestDeclarations(package.SourceRoot);
            var questBaseTypes = ReadQuestBaseTypes(package.SourceRoot);
            questMethods = questMethods.Select(method => method with { BaseTypes = questBaseTypes.GetValueOrDefault(ReturnTypeName(method.ReturnType), []) }).ToArray();
            var methodRows = new JsonArray();
            var mappings = new JsonArray();

            foreach (var pcMethod in pcMethods)
            {
                var pcSignature = PcMethodSignature.FromDefinition(pcMethod, assemblyName);
                var ranked = questMethods.Select(candidate => Evaluate(pcSignature, candidate)).Where(candidate => candidate is not null).Select(candidate => candidate!).OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Method.Header, StringComparer.Ordinal).ToArray();
                var best = ranked.FirstOrDefault();
                var secondScore = ranked.Skip(1).FirstOrDefault()?.Score ?? int.MinValue;
                var uniquelyMapped = best is not null && best.NameScore >= MinimumNameScore && best.Score >= MinimumNameScore && (secondScore == int.MinValue || best.Score - secondScore >= RequiredWinnerMargin);
                var status =
                    uniquelyMapped ? "mapped"
                    : ranked.Length == 0 ? "unmapped"
                    : "ambiguous";
                methodRows.Add(SerializePcMethod(pcMethod, status, ranked.Take(5)));
                if (uniquelyMapped)
                    mappings.Add(SerializeMapping(pcSignature, best!, package.Id));
            }

            libraries.Add(
                new JsonObject
                {
                    ["pcAssembly"] = assemblyName,
                    ["pcAssemblyPath"] = assemblyPath,
                    ["pcAssemblyVersion"] = assembly.Name.Version?.ToString(),
                    ["pcAssemblyMvid"] = assembly.MainModule.Mvid.ToString(),
                    ["questPackageId"] = package.Id,
                    ["questVersionRange"] = "=" + package.Version,
                    ["questPackageVersion"] = package.Version,
                    ["questPackageRoot"] = package.SourceRoot,
                    ["questApiSymbols"] = new JsonArray(questMethods.Select(SerializeQuestMethod).ToArray()),
                    ["discoveredPcMethods"] = methodRows,
                    ["methods"] = mappings,
                }
            );
        }

        if (sourceMod is not null)
        {
            var sourceCalls = ReadExternalCalls(sourceMod, references.ToHashSet(StringComparer.Ordinal));
            dependencyAdapters = DiscoverFrameworkAdapters(sourceMod, sourceCalls);
            var adapterHandledCalls = GetAdapterHandledCallKeys(sourceMod, sourceCalls);
            var structuralAssociations = generatedCandidatesPath is not null && File.Exists(generatedCandidatesPath) ? DiscoverStructuralAssociations(sourceMod, sourceCalls, packages, generatedCandidatesPath) : Array.Empty<StructuralAssociation>();
            var structuralTypeAssociations = generatedCandidatesPath is not null && File.Exists(generatedCandidatesPath) ? DiscoverUsedTypeAssociations(sourceCalls, references, packages, generatedCandidatesPath) : Array.Empty<StructuralTypeAssociation>();
            foreach (var association in structuralTypeAssociations)
            {
                typeAssociations.Add(
                    new JsonObject
                    {
                        ["pcAssembly"] = association.PcAssembly,
                        ["pcType"] = association.PcType,
                        ["questPackageId"] = association.Package.Id,
                        ["questPackageVersion"] = association.Package.Version,
                        ["questTypes"] = Strings(association.QuestTypes),
                        ["headers"] = Strings(association.Headers.Select(header => association.Package.Id + "/" + header)),
                        ["uniqueMethodCandidateCount"] = association.CandidateCount,
                        ["associationMode"] = "used-signature-type-with-unique-structural-candidates",
                    }
                );
            }
            foreach (var association in structuralAssociations.GroupBy(item => (item.PcAssembly, item.Package.Id)))
            {
                var package = association.First().Package;
                assemblyAssociations.Add(
                    new JsonObject
                    {
                        ["pcAssembly"] = association.Key.PcAssembly,
                        ["questPackageId"] = package.Id,
                        ["questPackageVersion"] = package.Version,
                        ["associationMode"] = "unique-used-structural-api-candidates",
                        ["mappedCandidateCount"] = association.Select(item => item.MethodKey).Distinct(StringComparer.Ordinal).Count(),
                        ["methods"] = new JsonArray(
                            association
                                .Select(item =>
                                    (JsonNode?)
                                        new JsonObject
                                        {
                                            ["pcType"] = item.PcType,
                                            ["pcMethod"] = item.PcMethod,
                                            ["questType"] = item.QuestType,
                                            ["questMethod"] = item.QuestMethod,
                                            ["questHeader"] = item.QuestHeader,
                                            ["resolution"] = item.Resolution,
                                        }
                                )
                                .ToArray()
                        ),
                    }
                );
            }
            var structuralCallKeys = structuralAssociations.Select(item => item.MethodKey).ToHashSet(StringComparer.Ordinal);
            var crossPackageCalls = sourceCalls.Where(call => !adapterHandledCalls.Contains(call.Key) && !packages.Any(package => PackageMatches(call.PcAssembly, package.Id)) && !structuralCallKeys.Contains(call.Key)).ToArray();
            var candidatePackages =
                crossPackageCalls.Length == 0 ? Array.Empty<QpmPackage>() : packages.Where(package => !consumedPackageIds.Contains(package.Id) && (!package.HeadersOnly || Directory.EnumerateFiles(package.SourceRoot, "*.hpp", SearchOption.AllDirectories).Take(10_001).Count() <= 10_000)).ToArray();
            var declarations = new Dictionary<string, IReadOnlyList<QuestMethod>>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in candidatePackages)
            {
                var tokens = crossPackageCalls.SelectMany(call => NameTokens(call.PcMethod)).Where(token => token.Length >= 3).ToHashSet(StringComparer.Ordinal);
                declarations[package.Id] = ReadQuestDeclarations(package.SourceRoot, tokens);
            }

            var inferred = new List<(PcMethodSignature Pc, QpmPackage Package, Inference Inference)>();
            foreach (var call in crossPackageCalls)
            {
                var ranked = candidatePackages
                    .SelectMany(package => declarations[package.Id].Select(method => (Package: package, Inference: Evaluate(call, method))))
                    .Where(item => item.Inference is not null)
                    .Select(item => (item.Package, Inference: item.Inference!))
                    .OrderByDescending(item => item.Inference.Score)
                    .ThenBy(item => item.Package.Id, StringComparer.Ordinal)
                    .ThenBy(item => item.Inference.Method.Header, StringComparer.Ordinal)
                    .ToArray();
                var best = ranked.FirstOrDefault();
                var secondScore = ranked.Skip(1).FirstOrDefault().Inference?.Score ?? int.MinValue;
                var unique = best.Inference is not null && best.Inference.NameScore >= MinimumNameScore && best.Inference.Score >= MinimumNameScore && (secondScore == int.MinValue || best.Inference.Score - secondScore >= RequiredWinnerMargin);
                if (unique)
                    inferred.Add((call, best.Package, best.Inference!));
                else
                    unresolvedCalls.Add(
                        new JsonObject
                        {
                            ["pcAssembly"] = call.PcAssembly,
                            ["pcType"] = call.PcType,
                            ["pcMethod"] = call.PcMethod,
                            ["status"] = ranked.Length == 0 ? "no_compatible_qpm_declaration" : "ambiguous_qpm_declaration",
                            ["candidates"] = new JsonArray(
                                ranked
                                    .Take(5)
                                    .Select(item =>
                                        (JsonNode?)
                                            new JsonObject
                                            {
                                                ["questPackageId"] = item.Package.Id,
                                                ["questMethod"] = item.Inference.Method.Name,
                                                ["questHeader"] = item.Inference.Method.Header,
                                                ["score"] = item.Inference.Score,
                                            }
                                    )
                                    .ToArray()
                            ),
                        }
                    );
            }

            foreach (var group in inferred.GroupBy(item => (item.Pc.PcAssembly, item.Package.Id)))
            {
                var package = group.First().Package;
                using var pcAssembly = AssemblyDefinition.ReadAssembly(QuestTargetProfileValidator.FindPcAssembly(pcManagedRoot, group.Key.PcAssembly)!);
                libraries.Add(
                    new JsonObject
                    {
                        ["pcAssembly"] = group.Key.PcAssembly,
                        ["pcAssemblyPath"] = QuestTargetProfileValidator.FindPcAssembly(pcManagedRoot, group.Key.PcAssembly),
                        ["pcAssemblyVersion"] = pcAssembly.Name.Version?.ToString(),
                        ["pcAssemblyMvid"] = pcAssembly.MainModule.Mvid.ToString(),
                        ["questPackageId"] = package.Id,
                        ["questVersionRange"] = "=" + package.Version,
                        ["questPackageVersion"] = package.Version,
                        ["questPackageRoot"] = package.SourceRoot,
                        ["associationMode"] = "unique-used-call-signature-inference",
                        ["questApiSymbols"] = new JsonArray(group.Select(item => (JsonNode?)SerializeQuestMethod(item.Inference.Method)).Distinct().ToArray()),
                        ["discoveredPcMethods"] = new JsonArray(
                            group
                                .Select(item =>
                                    (JsonNode?)
                                        new JsonObject
                                        {
                                            ["pcType"] = item.Pc.PcType,
                                            ["pcMethod"] = item.Pc.PcMethod,
                                            ["pcReturn"] = item.Pc.ReturnType.FullName,
                                            ["pcParameters"] = Strings(item.Pc.Parameters.Select(parameter => parameter.FullName)),
                                            ["pcStatic"] = item.Pc.IsStatic,
                                            ["status"] = "mapped",
                                        }
                                )
                                .ToArray()
                        ),
                        ["methods"] = new JsonArray(group.Select(item => (JsonNode?)SerializeMapping(item.Pc, item.Inference, package.Id)).ToArray()),
                    }
                );
            }
        }

        return Catalog(libraries, assemblyAssociations, typeAssociations, dependencyAdapters, unresolvedCalls);
    }

    private static string Catalog(JsonArray libraries, JsonArray assemblyAssociations, JsonArray typeAssociations, JsonArray dependencyAdapters, JsonArray unresolvedCalls) =>
        new JsonObject
        {
            ["schemaVersion"] = 2,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow,
            ["generationMode"] = "PC metadata and locally cached QPM declarations; only unique signature-compatible inferences are executable mappings",
            ["libraries"] = libraries,
            ["pcAssemblyAssociations"] = assemblyAssociations,
            ["pcTypeAssociations"] = typeAssociations,
            ["dependencyAdapters"] = dependencyAdapters,
            ["unresolvedReferencedCalls"] = unresolvedCalls,
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static IReadOnlyList<QpmPackage> DiscoverPackages(string cacheRoot)
    {
        var packages = new List<QpmPackage>();
        foreach (var manifest in Directory.EnumerateFiles(cacheRoot, "qpm.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                var info = document.RootElement.GetProperty("info");
                var id = info.GetProperty("id").GetString();
                var version = info.GetProperty("version").GetString();
                var headersOnly = info.TryGetProperty("additionalData", out var additionalData) && additionalData.ValueKind == JsonValueKind.Object && additionalData.TryGetProperty("headersOnly", out var headersOnlyValue) && headersOnlyValue.ValueKind == JsonValueKind.True;
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(version))
                    packages.Add(new QpmPackage(id!, version!, Path.GetDirectoryName(manifest)!, headersOnly));
            }
            catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        }
        return packages.GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.OrderByDescending(package => ParseVersion(package.Version)).ThenBy(package => package.SourceRoot, StringComparer.Ordinal).First()).ToArray();
    }

    private static Version ParseVersion(string value) => Version.TryParse(value.Split('-', '+')[0], out var version) ? version : new Version(0, 0);

    private static bool PackageMatches(string assembly, string package) => NormalizePackageName(assembly) == NormalizePackageName(package);

    private static string NormalizePackageName(string value) => Regex.Replace(value, "[^A-Za-z0-9]", "").ToLowerInvariant();
}
