using System.Text.RegularExpressions;
using Mono.Cecil;

namespace Transpiler;

internal static class QuestHeaderValidator
{
    public static void ValidateQuestHeader(IQuestMethodMapping mapping, string includeRoot)
    {
        var path = Path.Combine(includeRoot, mapping.DeclarationHeader.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            throw new InvalidOperationException($"Mapped bs-cordl declaration header is missing: {path}");
        var publicPath = Path.Combine(includeRoot, mapping.PublicHeader.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(publicPath))
            throw new InvalidOperationException($"Mapped bs-cordl public header is missing: {publicPath}");
        var text = File.ReadAllText(path);
        var parameters = string.Join(@"\s*,\s*", mapping.QuestParameters.Select(parameter => Regex.Escape(parameter) + @"\s+\w+"));
        var signature = Regex.Escape(mapping.QuestReturn + " " + mapping.QuestMethod + "(") + parameters + @"\s*\)";
        var declaration = Regex.Match(text, @"(?<prefix>(?:static\s+inline|inline\s+static|inline)\s+)" + signature + @"\s*;", RegexOptions.CultureInvariant);
        if (!declaration.Success)
            throw new InvalidOperationException($"Mapped declaration not found: {mapping.QuestReturn} {mapping.QuestMethod}({string.Join(", ", mapping.QuestParameters)}) in {path}.");
        var isStatic = declaration.Groups["prefix"].Value.Contains("static", StringComparison.Ordinal);
        if (isStatic != mapping.QuestStatic)
            throw new InvalidOperationException($"Mapped Quest static/instance shape does not match bs-cordl declaration in {path}.");
    }

    public static void AddMappedTypeHeaders(IQuestMethodMapping mapping, string includeRoot, ISet<string> requiredHeaders)
    {
        foreach (var signatureType in mapping.QuestParameters.Append(mapping.QuestReturn))
        foreach (Match match in Regex.Matches(signatureType, @"::(?<type>[A-Za-z_]\w*(?:::[A-Za-z_]\w*)*)", RegexOptions.CultureInvariant))
        {
            var relativeHeader = match.Groups["type"].Value.Replace("::", "/", StringComparison.Ordinal) + ".hpp";
            if (File.Exists(Path.Combine(includeRoot, relativeHeader.Replace('/', Path.DirectorySeparatorChar))))
                requiredHeaders.Add(relativeHeader);
        }
    }

    public static void AddQuestTypeHeader(TypeReference pcType, string includeRoot, ISet<string> requiredHeaders, string? generatedAssemblyName = null)
    {
        if (generatedAssemblyName is not null && pcType.Resolve()?.Module.Assembly.Name.Name == generatedAssemblyName)
        {
            var namespacePath = string.IsNullOrWhiteSpace(pcType.Namespace) ? "GlobalNamespace" : pcType.Namespace.Replace('.', '/');
            var typeParts = new Stack<string>();
            for (TypeReference? current = pcType; current is not null; current = current.DeclaringType)
                typeParts.Push(current.Name.Split('`')[0]);
            var typeName = string.Join("_", typeParts);
            requiredHeaders.Add($"{namespacePath}/{typeName}.hpp");
            return;
        }
        var questType = QuestApiTypeMapper.Map(pcType);
        if (questType is null || !questType.Contains("::", StringComparison.Ordinal))
            return;

        var nativeType = questType.TrimStart(':');
        var templateStart = nativeType.IndexOf('<');
        if (templateStart >= 0)
            nativeType = nativeType[..templateStart];
        nativeType = nativeType.TrimEnd('*').TrimEnd(':');
        var relativeHeader = nativeType.Replace("::", "/", StringComparison.Ordinal) + ".hpp";
        if (!File.Exists(Path.Combine(includeRoot, relativeHeader.Replace('/', Path.DirectorySeparatorChar))))
            throw new InvalidOperationException($"Quest type '{pcType.FullName}' maps to '{questType}', but no Quest declaration header exists at '{relativeHeader}'. The type must be ported or explicitly mapped before this patch can be emitted.");
        requiredHeaders.Add(relativeHeader);
    }
}
