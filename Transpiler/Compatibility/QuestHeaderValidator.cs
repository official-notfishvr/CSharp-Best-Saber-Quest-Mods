using System.Text.RegularExpressions;

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
}
