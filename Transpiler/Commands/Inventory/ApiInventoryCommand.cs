using System.Text.Json;
using Mono.Cecil;

namespace Transpiler;

internal static class ApiInventoryCommand
{
    public static async Task<int> RunAsync(string[] args, bool discoveryMode = false, bool quiet = false)
    {
        if (discoveryMode ? args.Length is not (3 or 4 or 5) : args.Length != 3)
        {
            Console.Error.WriteLine(
                "Usage: Transpiler inventory <pc-game-directory> <quest-bs-cordl-include-directory> <output.json>\n       Transpiler discover-mappings <pc-game-directory> <quest-bs-cordl-include-directory> <output-candidates.json> [output-inventory.json] [referenced-assemblies-semicolon-separated]"
            );
            return 2;
        }

        var pcRoot = Path.GetFullPath(args[0]);
        var questRoot = Path.GetFullPath(args[1]);
        var managedRoot = Directory.Exists(Path.Combine(pcRoot, "Beat Saber_Data", "Managed")) ? Path.Combine(pcRoot, "Beat Saber_Data", "Managed") : pcRoot;
        if (!Directory.Exists(managedRoot) || !Directory.Exists(questRoot))
        {
            Console.Error.WriteLine("PC managed assemblies or Quest include directory were not found.");
            return 2;
        }

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedRoot);
        var assemblyDirectories = new[] { managedRoot, Path.Combine(pcRoot, "Plugins"), Path.Combine(pcRoot, "Libs") };
        foreach (var directory in assemblyDirectories)
            if (Directory.Exists(directory))
                resolver.AddSearchDirectory(directory);

        var pcAssemblies = new List<AssemblySurface>();
        var scannedAssemblyNames = new HashSet<string>(StringComparer.Ordinal);
        var inventoryAssemblyFilter = discoveryMode && args.Length == 5 ? args[4].Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal) : null;
        foreach (var path in assemblyDirectories.Where(Directory.Exists).SelectMany(directory => Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (inventoryAssemblyFilter is not null && !inventoryAssemblyFilter.Contains(Path.GetFileNameWithoutExtension(path)))
                    continue;
                using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false });
                var name = assembly.Name.Name;
                if (scannedAssemblyNames.Add(name))
                {
                    var definitions = CecilTypeWalker.Flatten(assembly.MainModule.Types).ToList();
                    pcAssemblies.Add(new AssemblySurface(name, assembly.MainModule.Mvid, definitions.Select(TypeRecord.From).ToList(), definitions));
                }
            }
            catch (BadImageFormatException) { }
            catch (AssemblyResolutionException) { }
        }
        pcAssemblies = pcAssemblies.GroupBy(assembly => assembly.Name, StringComparer.Ordinal).Select(group => group.First()).ToList();

        var headerFiles = Directory.EnumerateFiles(questRoot, "*.hpp", SearchOption.AllDirectories).ToArray();
        var questTypeNames = headerFiles.SelectMany(path => new[] { Path.GetFileNameWithoutExtension(path), Path.GetFileNameWithoutExtension(path).TrimStart('z').TrimStart('_') }).ToHashSet(StringComparer.Ordinal);
        var types = pcAssemblies.SelectMany(assembly => assembly.Types).ToArray();
        var matched = types.Where(type => questTypeNames.Contains(type.Name)).ToArray();
        var pcTypeNames = types.Select(type => SimpleTypeName(type.FullName)).ToHashSet(StringComparer.Ordinal);
        var declarationHeaders = headerFiles.Where(path =>
        {
            var typeMatch = System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(path), @"^zzzz__(?<type>.+)_def$");
            return typeMatch.Success && pcTypeNames.Contains(typeMatch.Groups["type"].Value);
        });
        var questDeclarations = declarationHeaders.SelectMany(path => ReadQuestMethods(path, questRoot)).ToArray();
        var questMethodsByName = questDeclarations.GroupBy(method => (method.TypeName, method.Method)).ToDictionary(group => group.Key, group => group.ToArray());
        var exactMethodCandidates = pcAssemblies
            .SelectMany(assembly =>
                assembly.Definitions.SelectMany(type =>
                    type.Methods.Where(method => !method.IsConstructor && !method.IsAbstract)
                        .Select(method => new
                        {
                            Assembly = assembly.Name,
                            Type = type.FullName,
                            Method = method,
                        })
                )
            )
            .SelectMany(pc =>
            {
                var returnType = QuestApiTypeMapper.Map(pc.Method.ReturnType);
                var parameterTypes = pc.Method.Parameters.Select(parameter => QuestApiTypeMapper.Map(parameter.ParameterType)).ToArray();
                if (returnType is null || parameterTypes.Any(parameter => parameter is null))
                    return Array.Empty<object>();
                if (!questMethodsByName.TryGetValue((SimpleTypeName(pc.Type), pc.Method.Name), out var sameName))
                    return Array.Empty<object>();
                var matches = sameName.Where(quest => quest.IsStatic == pc.Method.IsStatic && QuestApiTypeMapper.QuestReturnMatches(returnType, quest.ReturnType) && quest.ParameterTypes.SequenceEqual(parameterTypes!)).ToArray();
                return matches.Select(quest =>
                    (object)
                        new
                        {
                            pcAssembly = pc.Assembly,
                            pcType = pc.Type,
                            pcMethod = pc.Method.Name,
                            pcGenericArity = pc.Method.GenericParameters.Count,
                            pcReturn = pc.Method.ReturnType.FullName,
                            pcParameters = pc.Method.Parameters.Select(parameter => parameter.ParameterType.FullName),
                            pcCppReturn = returnType,
                            pcCppParameters = parameterTypes,
                            pcStatic = pc.Method.IsStatic,
                            questType = quest.QualifiedType,
                            publicHeader = quest.PublicHeader,
                            declarationHeader = quest.DeclarationHeader,
                            questMethod = quest.Method,
                            questReturn = quest.ReturnType,
                            questParameters = quest.ParameterTypes,
                            questStatic = quest.IsStatic,
                            confidence = "exact-signature-candidate",
                            resolution = matches.Length == 1 ? "unique-structural-match" : "ambiguous-structural-match",
                            matchingQuestDeclarations = matches.Length,
                        }
                );
            })
            .ToArray();
        var report = new
        {
            schemaVersion = 2,
            pcInstall = pcRoot,
            questHeaders = questRoot,
            assembliesScanned = pcAssemblies.Count,
            questHeadersScanned = headerFiles.Length,
            pcTypeCount = types.Length,
            exactTypeNameMatches = matched.Length,
            exactMethodCandidates,
            typeCoverage = types.Length == 0 ? 0 : Math.Round((double)matched.Length / types.Length, 4),
            assemblies = pcAssemblies.Select(assembly => new { name = assembly.Name, types = assembly.Types.Count }),
            types = types.Select(type => new
            {
                assembly = type.Assembly,
                fullName = type.FullName,
                nativeHeaderCandidates = headerFiles.Where(path => Path.GetFileName(path).Contains(type.Name, StringComparison.Ordinal)).Select(path => Path.GetRelativePath(questRoot, path).Replace('\\', '/')).ToArray(),
                methods = type.Methods,
                fields = type.Fields,
            }),
        };

        var output = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (discoveryMode)
        {
            var catalog = new System.Text.Json.Nodes.JsonObject { ["schemaVersion"] = 2 };
            catalog["discovery"] = JsonSerializer.SerializeToNode(
                new
                {
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    pcInstall = pcRoot,
                    questHeaders = questRoot,
                    pcAssemblyFingerprints = pcAssemblies.Select(assembly => new { name = assembly.Name, mvid = assembly.Mvid }),
                    policy = "Candidates are conservative structural matches. They are not semantic approval and are not activated by the transpiler. Multiple Quest declarations matching a PC method remain separate candidates and require disambiguation.",
                    exactMethodCandidates,
                    unresolvedMethods = BuildUnresolvedMethods(pcAssemblies, questMethodsByName),
                }
            );
            await File.WriteAllTextAsync(output, catalog.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            if (args.Length >= 4)
            {
                var inventoryOutput = Path.GetFullPath(args[3]);
                Directory.CreateDirectory(Path.GetDirectoryName(inventoryOutput)!);
                await File.WriteAllTextAsync(inventoryOutput, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        else
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Scanned {pcAssemblies.Count} PC assemblies, {types.Length} types, and {headerFiles.Length} Quest headers.");
        if (quiet)
            Console.WriteLine($"Built {exactMethodCandidates.Length} exact-signature candidates for this transpilation run.");
        else
        {
            Console.WriteLine($"Exact class-name header candidates: {matched.Length} ({(types.Length == 0 ? 0 : matched.Length * 100.0 / types.Length):F1}%).");
            Console.WriteLine($"{(discoveryMode ? "Discovered mapping candidates" : "Inventory")}: {output}");
            Console.WriteLine("Name matches are candidates only; complete signatures, ABI, dependencies, and behavior require explicit compatibility mappings before C++ emission.");
            Console.WriteLine($"Exact PC/Quest method signature candidates: {exactMethodCandidates.Length}. These are generated candidates, not proof of semantic equivalence.");
        }
        return 0;
    }

    private static object[] BuildUnresolvedMethods(IEnumerable<AssemblySurface> assemblies, IReadOnlyDictionary<(string TypeName, string Method), QuestMethod[]> declarations) =>
        assemblies
            .SelectMany(assembly =>
                assembly.Definitions.SelectMany(type =>
                    type.Methods.Where(method => method.IsPublic && !method.IsConstructor)
                        .Select(method => new
                        {
                            assembly.Name,
                            Type = type,
                            Method = method,
                        })
                )
            )
            .Select(pc =>
            {
                if (!declarations.TryGetValue((SimpleTypeName(pc.Type.FullName), pc.Method.Name), out var sameName))
                    return null;
                var returnType = QuestApiTypeMapper.Map(pc.Method.ReturnType);
                var parameters = pc.Method.Parameters.Select(parameter => QuestApiTypeMapper.Map(parameter.ParameterType)).ToArray();
                var reason =
                    returnType is null || parameters.Any(parameter => parameter is null) ? "Unsupported or ambiguous PC type conversion"
                    : sameName.All(candidate => candidate.IsStatic != pc.Method.IsStatic) ? "Static/instance mismatch"
                    : sameName.Any(candidate => candidate.IsStatic == pc.Method.IsStatic && QuestApiTypeMapper.QuestReturnMatches(returnType, candidate.ReturnType) && candidate.ParameterTypes.SequenceEqual(parameters!)) ? "Matching Quest signature was not selected as a unique mapping"
                    : "Return or parameter signature differs after conservative type normalization";
                return (object)
                    new
                    {
                        pcAssembly = pc.Name,
                        pcType = pc.Type.FullName,
                        pcMethod = pc.Method.Name,
                        pcReturn = pc.Method.ReturnType.FullName,
                        pcParameters = pc.Method.Parameters.Select(parameter => parameter.ParameterType.FullName),
                        reason,
                        questCandidates = sameName.Select(candidate => new
                        {
                            candidate.QualifiedType,
                            candidate.PublicHeader,
                            candidate.DeclarationHeader,
                            candidate.Method,
                            candidate.ReturnType,
                            candidate.ParameterTypes,
                            candidate.IsStatic,
                        }),
                    };
            })
            .Where(item => item is not null)
            .Cast<object>()
            .ToArray();

    private static IEnumerable<QuestMethod> ReadQuestMethods(string path, string root)
    {
        var text = File.ReadAllText(path);
        var include = System.Text.RegularExpressions.Regex.Match(text, "IWYU pragma private; include \\\"([^\\\"]+)\\\"");
        if (!include.Success)
            yield break;
        var matchType = System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(path), @"^zzzz__(?<type>.+)_def$");
        if (!matchType.Success)
            yield break;
        var typeName = matchType.Groups["type"].Value;
        var publicHeader = include.Groups[1].Value.Replace('\\', '/');
        var namespacePath = Path.GetDirectoryName(publicHeader)?.Replace('\\', '/')?.Replace('/', ':');
        var ns = string.IsNullOrEmpty(namespacePath) ? "GlobalNamespace" : namespacePath.Replace(":", "::", StringComparison.Ordinal);
        var declaration = new System.Text.RegularExpressions.Regex(@"(?<prefix>(?:static\s+inline|inline\s+static|inline)\s+)(?<return>[A-Za-z_:][A-Za-z0-9_:<>*&\s]*?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<args>[^;{}]*)\)\s*;", System.Text.RegularExpressions.RegexOptions.Multiline);
        foreach (System.Text.RegularExpressions.Match method in declaration.Matches(text))
        {
            var args = method.Groups["args"].Value.Trim();
            var parameterTypes = args.Length == 0 ? Array.Empty<string>() : args.Split(',').Select(argument => System.Text.RegularExpressions.Regex.Replace(argument.Trim(), @"\s+[A-Za-z_][A-Za-z0-9_]*$", "").Trim()).ToArray();
            yield return new QuestMethod(
                ns + "::" + typeName,
                typeName,
                method.Groups["name"].Value,
                NormalizeCppType(method.Groups["return"].Value),
                parameterTypes.Select(NormalizeCppType).ToArray(),
                method.Groups["prefix"].Value.Contains("static", StringComparison.Ordinal),
                publicHeader,
                Path.GetRelativePath(root, path).Replace('\\', '/')
            );
        }
    }

    private static string NormalizeCppType(string type) => System.Text.RegularExpressions.Regex.Replace(type.Trim(), @"\s+", " ");

    private static string SimpleTypeName(string type) => type.Split('.').Last().Split('/').Last();

    private sealed record QuestMethod(string QualifiedType, string TypeName, string Method, string ReturnType, string[] ParameterTypes, bool IsStatic, string PublicHeader, string DeclarationHeader);

    private sealed record AssemblySurface(string Name, Guid Mvid, List<TypeRecord> Types, List<TypeDefinition> Definitions);

    private sealed record TypeRecord(string Assembly, string FullName, string Name, string[] Methods, string[] Fields)
    {
        public static TypeRecord From(TypeDefinition type) =>
            new(
                type.Module.Assembly.Name.Name,
                type.FullName,
                type.Name,
                type.Methods.Where(method => method.IsPublic && !method.IsConstructor).Select(method => $"{method.ReturnType.FullName} {method.Name}({string.Join(", ", method.Parameters.Select(parameter => parameter.ParameterType.FullName))})").Order(StringComparer.Ordinal).ToArray(),
                type.Fields.Where(field => field.IsPublic).Select(field => $"{field.FieldType.FullName} {field.Name}").Order(StringComparer.Ordinal).ToArray()
            );
    }
}
