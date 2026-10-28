using System.Text.Json;
using Mono.Cecil;

namespace Transpiler;

internal static partial class QuestTranspilationWorkflow
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 7 || args[0] is "-h" or "--help")
        {
            /*
            dotnet .\Transpiler\bin\Release\net8.0\Transpiler.dll quest 1.40.8 .\PcMod\SampleMod\bin\Release\net48\SampleMod.dll 'C:\Users\Administrator\BSManager\BSInstances\1.40.8\Beat Saber_Data\Managed' .\Old\quest-mod\extern\includes\bs-cordl\include .\Old\quest-mod .\GeneratedQuest\SampleMod
            
            dotnet .\Transpiler\bin\Release\net8.0\Transpiler.dll quest 1.40.8 .\PcMod\PauseKey\bin\Release\net48\PauseKey.dll 'C:\Users\Administrator\BSManager\BSInstances\1.40.8\Beat Saber_Data\Managed' .\Old\quest-mod\extern\includes\bs-cordl\include .\Old\quest-mod .\GeneratedQuest\PauseKey
        
            dotnet .\Transpiler\bin\Release\net8.0\Transpiler.dll quest 1.40.8 .\PcMod\HighlightBombs\bin\Release\net48\HighlightBombs.dll 'C:\Users\Administrator\BSManager\BSInstances\1.40.8\Beat Saber_Data\Managed' .\Old\quest-mod\extern\includes\bs-cordl\include .\Old\quest-mod .\GeneratedQuest\HighlightBombs
        
        */

            Console.Error.WriteLine(
                "Usage:\n  Transpiler inventory <pc-game-directory> <quest-bs-cordl-include-directory> <output.json>\n  Transpiler discover-mappings <pc-game-directory> <quest-bs-cordl-include-directory> <output-candidates.json> [output-inventory.json]\n  Transpiler quest <pc-game-version> <pc-mod.dll> <pc-managed-directory> <bs-cordl-include-root> <quest-template-root> <output-directory>"
            );
            return args.Length == 0 || args[0] is "-h" or "--help" ? 0 : 2;
        }
        if (args[0] != "quest")
        {
            Console.Error.WriteLine($"Unknown target '{args[0]}'.");
            return 2;
        }

        string? requestedOutputDirectory = null;
        try
        {
            var pcGameVersion = args[1];
            var assemblyPath = Path.GetFullPath(args[2]);
            var pcManaged = Path.GetFullPath(args[3]);
            var headers = Path.GetFullPath(args[4]);
            var template = Path.GetFullPath(args[5]);
            var output = Path.GetFullPath(args[6]);
            requestedOutputDirectory = output;
            if (!File.Exists(assemblyPath) || !Directory.Exists(pcManaged) || !Directory.Exists(headers) || !Directory.Exists(template))
                throw new InvalidOperationException("The PC assembly, PC managed directory, bs-cordl include root, or Quest template root does not exist.");

            using var assemblyResolver = PcAssemblyResolver.Create(pcManaged, assemblyPath);
            using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { AssemblyResolver = assemblyResolver, ReadSymbols = false });
            var metadata = ReadModMetadata(assembly);
            var embeddedAssetBundles = EmbeddedAssetBundleBootstrapper.Discover(assembly, metadata.Id);
            var zenjectBootstrap = ZenjectBootstrapLowerer.Lower(assembly, embeddedAssetBundles);
            var gameDataDirectory = Directory.GetParent(pcManaged)?.FullName;
            var pcGameRoot = gameDataDirectory is null ? null : Directory.GetParent(gameDataDirectory)?.FullName;
            if (pcGameRoot is null)
                throw new InvalidOperationException("Could not determine the PC game root for automatic PC-to-Quest API discovery.");
            Directory.CreateDirectory(output);
            var initialReportPath = Path.Combine(output, "transpilation-report.json");
            File.WriteAllText(
                initialReportPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 2,
                        status = "pending",
                        sourceAssembly = assembly.Name.Name,
                    },
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
            var discoveryCacheDirectory = Path.Combine(output, ".transpiler-cache");
            Directory.CreateDirectory(discoveryCacheDirectory);
            var generatedCandidatesPath = Path.Combine(discoveryCacheDirectory, "pc-quest-api-candidates.json");
            var discoveryStampPath = Path.Combine(discoveryCacheDirectory, "input-stamp.txt");
            var apiInventoryPath = Path.Combine(output, "pc-quest-api-inventory.json");
            var referencedAssemblyNames = assembly
                .MainModule.AssemblyReferences.Select(reference => reference.Name)
                .Where(name => name is not ("mscorlib" or "System" or "System.Core" or "netstandard") && !name.StartsWith("System.", StringComparison.Ordinal) && !name.StartsWith("Microsoft.", StringComparison.Ordinal))
                .ToArray();
            var discoveryStamp = TranspilerCache.ComputeDiscoveryInputStamp(pcGameRoot, headers, pcGameVersion, referencedAssemblyNames);
            MappingCatalog catalog;
            string generatedLibraryMappings;
            if (File.Exists(generatedCandidatesPath) && File.Exists(discoveryStampPath) && File.Exists(apiInventoryPath) && File.ReadAllText(discoveryStampPath) == discoveryStamp)
            {
                Console.WriteLine("Reusing cached PC/Quest API discovery from the output folder.");
                catalog = MappingCatalog.Discover(pcGameVersion, generatedCandidatesPath, pcManaged, template);
                generatedLibraryMappings = QuestLibraryMappingGenerator.Generate(pcManaged, QpmCache.Root, referencedAssemblyNames, assembly, generatedCandidatesPath);
                TranspilerCache.PersistQuestLibraryMappings(output, generatedLibraryMappings);
                catalog.AddQuestLibraryMappings(generatedLibraryMappings);
            }
            else
            {
                var discoveryResult = await ApiInventoryCommand.RunAsync(new[] { pcGameRoot, headers, generatedCandidatesPath, apiInventoryPath, string.Join(";", referencedAssemblyNames) }, discoveryMode: true, quiet: true);
                if (discoveryResult != 0)
                    throw new InvalidOperationException("Automatic PC-to-Quest API discovery failed.");
                File.WriteAllText(discoveryStampPath, discoveryStamp);
                catalog = MappingCatalog.Discover(pcGameVersion, generatedCandidatesPath, pcManaged, template);
                generatedLibraryMappings = QuestLibraryMappingGenerator.Generate(pcManaged, QpmCache.Root, referencedAssemblyNames, assembly, generatedCandidatesPath);
                TranspilerCache.PersistQuestLibraryMappings(output, generatedLibraryMappings);
                catalog.AddQuestLibraryMappings(generatedLibraryMappings);
            }
            QuestTargetProfileValidator.ValidatePcAssemblies(catalog.Profile, pcManaged);
            QuestTargetProfileValidator.Validate(catalog.Profile, template);
            var patchDiscovery = DiscoverPatches(assembly);
            var patches = patchDiscovery.Patches;
            var bsmlResult = BsmlSettingsLowerer.Lower(assembly, metadata.Id, catalog);
            var bsmlMenus = bsmlResult.Menus;
            var lifecycle = PluginLifecycleLowerer.Lower(assembly);
            if (patches.Count == 0 && bsmlMenus.Count == 0 && lifecycle.Operations.Count == 0)
                throw new InvalidOperationException("No supported Quest features were found (supported Harmony hooks or BSML settings menus).");

            var pcAssemblies = new List<AssemblyDefinition>();
            foreach (var name in catalog.PcAssemblies)
            {
                var path = QuestTargetProfileValidator.FindPcAssembly(pcManaged, name);
                if (path is null)
                    throw new InvalidOperationException($"Mapped PC assembly '{name}.dll' is missing from the selected game's Managed, Plugins, and Libs directories.");
                pcAssemblies.Add(AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = assemblyResolver, ReadSymbols = false }));
            }

            var translatedPatches = new List<TranslatedHarmonyPatch>();
            var unsupportedOperations = new List<string>(patchDiscovery.Unsupported.Concat(bsmlResult.Unsupported));
            unsupportedOperations.AddRange(zenjectBootstrap.Unsupported);
            var mappingReport = new List<object>();
            var resolvedMappings = new List<TargetMapping>();
            var requiredApiHeaders = new HashSet<string>(StringComparer.Ordinal);
            var questConfigFields = new Dictionary<string, QuestConfigField>(StringComparer.Ordinal);
            var questStaticFields = new Dictionary<string, QuestStaticField>(StringComparer.Ordinal);
            foreach (var patch in patches)
            {
                try
                {
                    var pcTarget = ResolvePcTarget(patch, pcAssemblies, catalog);
                    var mapping = catalog.Resolve(pcTarget);
                    resolvedMappings.Add(mapping);
                    var translatedLogs = IlMethodTranslator.Translate(patch, catalog, mapping, pcTarget);
                    foreach (var field in translatedLogs.Select(statement => statement.ConfigField).OfType<QuestConfigField>())
                    {
                        if (questConfigFields.TryGetValue(field.Name, out var prior) && prior != field)
                            throw new InvalidOperationException($"Quest config field name collision: {field.Name}.");
                        questConfigFields[field.Name] = field;
                    }
                    foreach (var field in translatedLogs.SelectMany(statement => statement.StaticFields ?? Array.Empty<QuestStaticField>()))
                    {
                        if (questStaticFields.TryGetValue(field.Name, out var prior) && prior != field)
                            throw new InvalidOperationException($"Quest static field name collision: {field.Name}.");
                        questStaticFields[field.Name] = field;
                    }
                    QuestHeaderValidator.ValidateQuestHeader(mapping, headers);
                    QuestHeaderValidator.AddMappedTypeHeaders(mapping, headers, requiredApiHeaders);
                    foreach (
                        var referencedType in patch.Method.Body.Instructions.SelectMany(instruction =>
                        {
                            var types = new List<TypeReference>();
                            if (instruction.Operand is TypeReference typeOperand)
                                types.Add(typeOperand);
                            if (instruction.Operand is GenericInstanceMethod genericMethod)
                                types.AddRange(genericMethod.GenericArguments);
                            return types;
                        })
                    )
                        QuestHeaderValidator.AddQuestTypeHeader(referencedType, headers, requiredApiHeaders, assembly.Name.Name);
                    var callMappings = patch
                        .Method.Body.Instructions.Where(instruction => instruction.OpCode.Code is Mono.Cecil.Cil.Code.Call or Mono.Cecil.Cil.Code.Callvirt)
                        .Select(instruction => catalog.ResolveApiCall((MethodReference)instruction.Operand))
                        .Concat(translatedLogs.Select(statement => statement.ApiMapping))
                        .OfType<ApiCallMapping>()
                        .DistinctBy(call => call.PublicHeader + "|" + call.QuestMethod)
                        .ToList();
                    foreach (var callMapping in callMappings)
                    {
                        if (callMapping.IsQuestLibraryMapping)
                        {
                            var packageIncludePrefix = callMapping.QuestPackageId + "/";
                            var packageHeader = callMapping.PublicHeader.StartsWith(packageIncludePrefix, StringComparison.Ordinal) ? callMapping.PublicHeader[packageIncludePrefix.Length..] : callMapping.PublicHeader;
                            if (!File.Exists(Path.Combine(QpmCache.Root, callMapping.QuestPackageId!, callMapping.QuestPackageVersionRange!.TrimStart('=', '^'), "src", packageHeader.Replace('/', Path.DirectorySeparatorChar))))
                                throw new InvalidOperationException($"Quest package '{callMapping.QuestPackageId}' {callMapping.QuestPackageVersionRange} is not present in the local QPM cache with header '{callMapping.PublicHeader}'.");
                        }
                        else
                        {
                            QuestHeaderValidator.ValidateQuestHeader(callMapping, headers);
                        }
                        requiredApiHeaders.Add(callMapping.PublicHeader);
                        QuestHeaderValidator.AddMappedTypeHeaders(callMapping, headers, requiredApiHeaders);
                    }
                    translatedPatches.Add(new TranslatedHarmonyPatch(mapping, patch, translatedLogs));
                    mappingReport.Add(
                        new
                        {
                            pcAssembly = pcTarget.Module.Assembly.Name.Name,
                            pcType = pcTarget.DeclaringType.FullName,
                            pcMethod = pcTarget.Name,
                            pcReturn = TypeName(pcTarget.ReturnType),
                            pcParameters = pcTarget.Parameters.Select(p => TypeName(p.ParameterType)),
                            questType = mapping.QuestType,
                            questMethod = mapping.QuestMethod,
                            questHeader = mapping.PublicHeader,
                            mappingStatus = "validated",
                            loweredCalls = translatedLogs.Select(log => new { source = log.SourceCall, quest = "PaperLogger." + log.QuestLevel }),
                            mappedApiCalls = callMappings.Select(call => new
                            {
                                pcAssembly = call.PcAssembly,
                                pcType = call.PcType,
                                pcMethod = call.PcMethod,
                                questType = call.QuestType,
                                questMethod = call.QuestMethod,
                                questHeader = call.PublicHeader,
                            }),
                        }
                    );
                }
                catch (InvalidOperationException ex)
                {
                    unsupportedOperations.Add($"{patch.DeclaringType.FullName}.{patch.Method.Name}: {ex.Message}");
                }
            }

            var groupedHooks = translatedPatches.GroupBy(item => HookTargetKey(item.Mapping), StringComparer.Ordinal).Select(group => group.ToArray()).ToArray();
            var generatedHooks = groupedHooks.Select(group => GenerateGroupedHook(group[0].Mapping, group)).ToList();
            generatedHooks.AddRange(embeddedAssetBundles.HookCode);
            var hookNames = groupedHooks.Select(group => group[0].Mapping.HookName).Concat(embeddedAssetBundles.HookNames).ToArray();

            var bsmlOutput = BsmlQuestEmitter.Emit(bsmlMenus, assembly.Name.Name);
            foreach (var callMapping in bsmlOutput.ApiMappings)
            {
                QuestHeaderValidator.ValidateQuestHeader(callMapping, headers);
                requiredApiHeaders.Add(callMapping.PublicHeader);
                QuestHeaderValidator.AddMappedTypeHeaders(callMapping, headers, requiredApiHeaders);
            }
            foreach (var field in bsmlOutput.ConfigFields)
            {
                if (questConfigFields.TryGetValue(field.Name, out var prior) && prior != field)
                    throw new InvalidOperationException($"Quest config field name collision: {field.Name}.");
                questConfigFields[field.Name] = field;
            }

            var localTypeReferences = patches
                .SelectMany(patch => patch.Method.Body.Instructions)
                .SelectMany(instruction =>
                    instruction.Operand switch
                    {
                        TypeReference type => new[] { type },
                        GenericInstanceMethod generic => generic.GenericArguments.ToArray(),
                        _ => Array.Empty<TypeReference>(),
                    }
                )
                .Where(type =>
                {
                    try
                    {
                        return type.Resolve()?.Module.Assembly.Name.Name == assembly.Name.Name;
                    }
                    catch (ResolutionException)
                    {
                        return false;
                    }
                })
                .ToArray();
            IReadOnlyList<GeneratedModType> generatedTypes;
            try
            {
                generatedTypes = GeneratedModTypeEmitter.Emit(localTypeReferences, assembly, catalog, headers);
            }
            catch (InvalidOperationException ex)
            {
                unsupportedOperations.Add("Generated mod type: " + ex.Message);
                generatedTypes = Array.Empty<GeneratedModType>();
            }
            if (unsupportedOperations.Count > 0)
                throw new InvalidOperationException($"Quest generation refused to emit a partial mod. Unsupported operations remain:\n - {string.Join("\n - ", unsupportedOperations.Distinct(StringComparer.Ordinal))}");

            CopyTemplate(template, output);
            WriteEmbeddedAssetBundles(output, embeddedAssetBundles);
            AddEmbeddedAssetBundleEmbedding(Path.Combine(output, "CMakeLists.txt"), embeddedAssetBundles);
            foreach (var generatedType in generatedTypes)
            {
                var headerPath = Path.Combine(output, "include", generatedType.HeaderPath.Replace('/', Path.DirectorySeparatorChar));
                var sourcePath = Path.Combine(output, "src", Path.ChangeExtension(generatedType.HeaderPath, ".cpp").Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(headerPath)!);
                Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
                File.WriteAllText(headerPath, generatedType.Header);
                File.WriteAllText(sourcePath, generatedType.Source);
            }
            if (bsmlMenus.Count > 0)
                AddBsmlIncludeDirectory(Path.Combine(output, "CMakeLists.txt"));
            if (questConfigFields.Count > 0)
                File.WriteAllText(Path.Combine(output, "include", "TranspiledModConfig.hpp"), GenerateConfigHeader(questConfigFields.Values));
            UpdateLoggerContext(Path.Combine(output, "include", "main.hpp"), metadata.Id);
            var requiredHeaders = resolvedMappings.Select(m => m.PublicHeader).Concat(requiredApiHeaders).Concat(generatedTypes.Select(type => type.HeaderPath)).Distinct(StringComparer.Ordinal);
            File.WriteAllText(
                Path.Combine(output, "src", "main.cpp"),
                GenerateSource(
                    string.Join("\n\n", generatedHooks),
                    hookNames,
                    requiredHeaders.Concat(bsmlOutput.Includes).Concat(embeddedAssetBundles.Includes).Concat(zenjectBootstrap.Includes),
                    metadata,
                    questConfigFields.Count > 0,
                    questStaticFields.Values,
                    string.Join("\n", embeddedAssetBundles.SupportCode.Concat(new[] { bsmlOutput.SupportCode }.Where(value => !string.IsNullOrWhiteSpace(value)))),
                    zenjectBootstrap.StartupComments
                        .Concat(embeddedAssetBundles.StartupLines)
                        .Concat(bsmlOutput.StartupLines),
                    bsmlMenus.Count,
                    lifecycle.NeedsUnload
                )
            );
            UpdateProjectMetadata(Path.Combine(output, "qpm.json"), metadata);
            UpdateProjectMetadata(Path.Combine(output, "mod.template.json"), metadata);
            UpdateQmodManifest(Path.Combine(output, "mod.json"), metadata, embeddedAssetBundles);
            UpdateQmodManifest(Path.Combine(output, "mod.template.json"), metadata, embeddedAssetBundles);
            var report = new
            {
                schemaVersion = 2,
                profile = catalog.Profile,
                sourceAssembly = assembly.Name.Name,
                coverage = new
                {
                    types = CecilTypeWalker.Flatten(assembly.MainModule.Types).Count(),
                    methodsWithBodies = CecilTypeWalker.Flatten(assembly.MainModule.Types).SelectMany(type => type.Methods).Count(method => method.HasBody),
                    generatedHarmonyHooks = generatedHooks.Count,
                    externalAssemblies = assembly.MainModule.AssemblyReferences.Select(reference => reference.Name).Order(StringComparer.Ordinal),
                },
                lifecycle = new
                {
                    operations = lifecycle.Operations,
                    unsupported = lifecycle.Operations.Where(operation => operation.Status == "unsupported"),
                    approximations = lifecycle.Operations.Where(operation => operation.Status == "approximated"),
                },
                embeddedAssetBundles = embeddedAssetBundles.Bundles.Select(bundle => new
                {
                    resource = bundle.ResourceName,
                    packagePath = bundle.PackagePath,
                    devicePath = bundle.DevicePath,
                    asset = bundle.AssetName,
                    targetField = bundle.TargetField.FullName,
                }),
                zenjectInstallers = zenjectBootstrap.Installers,
                unsupported = unsupportedOperations,
                patches = mappingReport,
            };
            File.WriteAllText(Path.Combine(output, "transpilation-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            foreach (var pcAssembly in pcAssemblies)
                pcAssembly.Dispose();
            Console.WriteLine($"Generated {generatedHooks.Count} Harmony hook(s) (prefixes and postfixes) and {bsmlMenus.Count} BSML settings menu(s); unsupported operations: {unsupportedOperations.Count}.");
            Console.WriteLine($"Output: {output}");
            return 0;
        }
        catch (Exception ex)
        {
            if (requestedOutputDirectory is not null && Directory.Exists(requestedOutputDirectory))
            {
                var reportPath = Path.Combine(requestedOutputDirectory, "transpilation-report.json");
                if (!File.Exists(reportPath) || File.ReadAllText(reportPath).Contains("\"status\": \"pending\"", StringComparison.Ordinal))
                    File.WriteAllText(
                        reportPath,
                        JsonSerializer.Serialize(
                            new
                            {
                                schemaVersion = 2,
                                status = "failed",
                                error = ex.Message,
                            },
                            new JsonSerializerOptions { WriteIndented = true }
                        )
                    );
            }
            Console.Error.WriteLine($"Quest generation failed: {ex.Message}");
            return 1;
        }
    }
}
