using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace Transpiler;

internal static partial class QuestLibraryMappingGenerator
{
    private static Inference? Evaluate(PcMethodSignature pc, QuestMethod quest)
    {
        if (pc.GenericArity != 0 || pc.Parameters.Length != quest.Parameters.Length || pc.IsStatic != quest.IsStatic)
            return null;
        var conversions = new string[pc.Parameters.Length];
        for (var index = 0; index < conversions.Length; index++)
        {
            var conversion = InferArgumentConversion(pc.Parameters[index], quest.Parameters[index]);
            if (conversion is null)
                return null;
            conversions[index] = conversion;
        }

        var returnConversion = InferReturnConversion(pc.ReturnType, quest, pc.PcMethod);
        if (returnConversion is Unsupported)
            return null;
        var nameScore = NameScore(pc.PcMethod, quest.Name);
        if (nameScore == 0)
            return null;
        var score = nameScore + 20 + conversions.Length * 5 + (returnConversion is null ? 0 : 8);
        if (pc.IsStatic == quest.IsStatic)
            score += 5;
        return new Inference(quest, score, conversions, returnConversion as string, nameScore);
    }

    private static string? InferArgumentConversion(TypeReference pcType, string questType)
    {
        var pc = pcType.FullName;
        if (pc == "System.String" && questType is "std::string_view" or "std::string")
            return "StringToUtf8StringView";
        var normalizedPc = NormalizePcPrimitive(pc);
        if (normalizedPc is not null && normalizedPc == StripCvAndReference(questType))
            return "";
        var pcName = NormalizeTypeName(pcType.Name);
        var questName = NormalizeTypeName(StripCvAndReference(questType).Split(new[] { "::", "<", "*" }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "");
        if (pcName.Length > 0 && pcName == questName)
            return "Identity";
        return null;
    }

    private static object? InferReturnConversion(TypeReference pcType, QuestMethod questMethod, string pcMethod)
    {
        var pc = pcType.FullName;
        var quest = questMethod.ReturnType.Trim();
        if (pc == "System.Void" && quest == "void")
            return null;
        if (pc == "System.Boolean" && quest == "bool")
            return null;
        if (pc == "System.String" && quest is "std::string" or "std::string_view")
            return "StringViewToStringW";
        if (pc == "System.Boolean" && quest.EndsWith('*') && HasPredicateIntent(pcMethod) && HasLookupIntent(questMethod.Name))
            return "PointerIsNonNull";
        var primitive = NormalizePcPrimitive(pc);
        if (primitive is not null && primitive == StripCvAndReference(quest))
            return null;
        if (pc is not ("System.Void" or "System.Boolean" or "System.String"))
        {
            var pcName = NormalizeTypeName(pcType.Name);
            var questName = NormalizeTypeName(quest.Split(new[] { "::", "<", "*" }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "");
            if (pcName == questName || quest.EndsWith('*') && questName.Length > 0 && pcName == questName)
                return null;
            if (quest.EndsWith('*') && questMethod.BaseTypes.Any(baseType => NormalizeTypeName(baseType) == NormalizeTypeName(pcType.Name)))
                return null;
        }
        return Unsupported.Value;
    }

    private static string ReturnTypeName(string type) => NormalizeTypeName(type.Split(new[] { "::", "<", "*" }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "");

    private static Dictionary<string, string[]> ReadQuestBaseTypes(string packageRoot)
    {
        var bases = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(packageRoot, "*.hpp", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(text, @"DECLARE_CLASS_CODEGEN(?:_INTERFACES)?\(\s*(?<ns>[\w:]+)\s*,\s*(?<name>\w+)\s*,\s*(?<base>[^,)]+)"))
            {
                var name = NormalizeTypeName(match.Groups["name"].Value);
                var baseName = NormalizeTypeName(match.Groups["base"].Value.Split("::").Last());
                bases[name] = [baseName];
            }
        }
        return bases;
    }

    private static string StripCvAndReference(string type) => Regex.Replace(type.Trim(), @"\b(const|volatile)\b", "").Replace("&", "").Trim();

    private static string? NormalizePcPrimitive(string name) =>
        name switch
        {
            "System.Void" => "void",
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
            _ => null,
        };

    private static int NameScore(string pcName, string questName)
    {
        var pc = NameTokens(pcName);
        var quest = NameTokens(questName);
        if (pc.SequenceEqual(quest))
            return 100;
        if (pc.Length == 0 || quest.Length == 0)
            return 0;
        var overlap = pc.Intersect(quest, StringComparer.Ordinal).Count();
        var union = pc.Union(quest, StringComparer.Ordinal).Count();
        var score = union == 0 ? 0 : overlap * 100 / union;
        if (pc.Contains("present") && quest.Contains("get") || pc.Contains("exists") && quest.Contains("get"))
            score += 25;
        if (pc.Contains("register") && quest.Contains("register"))
            score += 15;
        return Math.Min(score, 100);
    }

    private static string[] NameTokens(string value)
    {
        var separated = Regex.Replace(value, "([A-Z]+)([A-Z][a-z])", "$1 $2");
        separated = Regex.Replace(separated, "([a-z0-9])([A-Z])", "$1 $2");
        var tokens = separated
            .ToLowerInvariant()
            .Split(new[] { '_', ' ', '-', ':' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token =>
                token switch
                {
                    "deregister" => "unregister",
                    "unregistration" => "unregister",
                    "id" => "",
                    "by" or "from" or "with" or "the" or "custom" or "song" => "",
                    _ => token,
                }
            )
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return tokens;
    }

    private static bool HasPredicateIntent(string name) => NameTokens(name).Any(token => token is "present" or "exists" or "contains" or "has" or "is");

    private static bool HasLookupIntent(string name) => NameTokens(name).Any(token => token is "get" or "find" or "lookup" or "resolve");

    private static string NormalizeTypeName(string value) => Regex.Replace(value.Split('.').LastOrDefault() ?? value, "[^A-Za-z0-9]", "").ToLowerInvariant();

    private static JsonObject SerializeMapping(PcMethodSignature pc, Inference inference, string packageId)
    {
        var quest = inference.Method;
        var conversions = inference.ArgumentConversions;
        var result = new JsonObject
        {
            ["pcType"] = pc.PcType,
            ["pcMethod"] = pc.PcMethod,
            ["pcGenericArity"] = pc.GenericArity,
            ["pcReturn"] = pc.ReturnType.FullName,
            ["pcParameters"] = Strings(pc.Parameters.Select(parameter => parameter.FullName)),
            ["pcStatic"] = pc.IsStatic,
            ["questType"] = quest.Owner,
            ["questHeader"] = packageId + "/" + quest.Header,
            ["questMethod"] = quest.Name,
            ["questReturn"] = quest.ReturnType,
            ["questParameters"] = Strings(quest.Parameters),
            ["questStatic"] = quest.IsStatic,
            ["argumentConversions"] = Strings(conversions),
            ["mappingStatus"] = "inferred_unique",
            ["mappingEvidence"] = new JsonObject
            {
                ["nameScore"] = inference.NameScore,
                ["candidateScore"] = inference.Score,
                ["alternateHeaders"] = Strings(quest.AlternateHeaders),
            },
        };
        if (inference.ReturnConversion is not null)
            result["returnConversion"] = inference.ReturnConversion;
        return result;
    }

    private static JsonObject SerializePcMethod(MethodDefinition method, string status, IEnumerable<Inference> candidates) =>
        new()
        {
            ["pcType"] = method.DeclaringType.FullName,
            ["pcMethod"] = method.Name,
            ["pcReturn"] = method.ReturnType.FullName,
            ["pcParameters"] = Strings(method.Parameters.Select(parameter => parameter.ParameterType.FullName)),
            ["pcStatic"] = method.IsStatic,
            ["status"] = status,
            ["questCandidates"] = new JsonArray(
                candidates
                    .Select(candidate =>
                        (JsonNode?)
                            new JsonObject
                            {
                                ["questType"] = candidate.Method.Owner,
                                ["questMethod"] = candidate.Method.Name,
                                ["questHeader"] = candidate.Method.Header,
                                ["questReturn"] = candidate.Method.ReturnType,
                                ["questParameters"] = Strings(candidate.Method.Parameters),
                                ["score"] = candidate.Score,
                                ["nameScore"] = candidate.NameScore,
                            }
                    )
                    .ToArray()
            ),
        };

    private static JsonObject SerializeQuestMethod(QuestMethod method) =>
        new()
        {
            ["name"] = method.Name,
            ["questType"] = method.Owner,
            ["header"] = method.Header,
            ["declaration"] = method.Declaration,
            ["returnType"] = method.ReturnType,
            ["parameterTypes"] = Strings(method.Parameters),
            ["isStatic"] = method.IsStatic,
            ["alternateHeaders"] = Strings(method.AlternateHeaders),
        };

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
}
