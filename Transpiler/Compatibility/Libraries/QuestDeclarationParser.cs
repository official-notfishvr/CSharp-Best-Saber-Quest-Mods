using System.Text.RegularExpressions;

namespace Transpiler;

internal static partial class QuestLibraryMappingGenerator
{
    private static IReadOnlyList<QuestMethod> ReadQuestDeclarations(string packageRoot, IReadOnlySet<string>? declarationTokens = null)
    {
        var result = new List<QuestMethod>();
        foreach (var path in Directory.EnumerateFiles(packageRoot, "*.hpp", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var header = Path.GetRelativePath(packageRoot, path).Replace('\\', '/');
            var lines = File.ReadAllLines(path);
            if (declarationTokens is { Count: > 0 } && !declarationTokens.Any(token => lines.Any(line => line.Contains(token, StringComparison.OrdinalIgnoreCase))))
                continue;
            var scope = new CppScopeTracker();
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = StripComments(lines[lineIndex]).Trim();
                scope.Observe(line);
                if (!line.EndsWith(';') || !line.Contains('(') || line.StartsWith('#') || line.StartsWith("__declspec(property", StringComparison.Ordinal))
                    continue;
                var declaration = ReadLogicalDeclaration(lines, lineIndex);
                if (!TryParseFunction(declaration, scope, header, out var method))
                    continue;
                result.Add(method);
            }
        }

        return result
            .GroupBy(method => method.TargetKey, StringComparer.Ordinal)
            .Select(group => group.OrderBy(method => HeaderPreference(method.Header)).ThenBy(method => method.Header, StringComparer.Ordinal).First() with { AlternateHeaders = group.Select(method => method.Header).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() })
            .ToArray();
    }

    private static int HeaderPreference(string header) => header.StartsWith("shared/", StringComparison.Ordinal) ? 0 : 1;

    private static string ReadLogicalDeclaration(string[] lines, int lineIndex)
    {
        var chunks = new List<string>();
        for (var index = lineIndex; index < lines.Length && index <= lineIndex + 16; index++)
        {
            var clean = StripComments(lines[index]).Trim();
            if (clean.Length > 0)
                chunks.Add(clean);
            if (clean.Contains(';'))
                break;
        }
        return string.Join(" ", chunks);
    }

    private static string StripComments(string line) => Regex.Replace(line, @"//.*$", "").Trim();

    private static bool TryParseFunction(string declaration, CppScopeTracker scope, string header, out QuestMethod method)
    {
        method = default!;
        if (declaration.Contains("__declspec(property", StringComparison.Ordinal) || declaration.StartsWith("using ", StringComparison.Ordinal) || declaration.StartsWith("typedef ", StringComparison.Ordinal) || declaration.StartsWith("return ", StringComparison.Ordinal))
            return false;

        var paren = declaration.IndexOf('(');
        if (paren <= 0)
            return false;
        var nameMatch = Regex.Match(declaration[..paren], @"(?<name>~?[A-Za-z_]\w*)\s*$");
        if (!nameMatch.Success)
            return false;
        var name = nameMatch.Groups["name"].Value;
        if (name is "if" or "while" or "switch" or "for" or "catch" or "requires" or "static_assert")
            return false;
        var closeParen = FindMatching(declaration, paren, '(', ')');
        if (closeParen < 0)
            return false;
        var prefix = declaration[..nameMatch.Index].Trim();
        if (prefix.Length == 0 || prefix.Contains('=') || prefix.Contains("operator", StringComparison.Ordinal))
            return false;
        var isStatic = Regex.IsMatch(prefix, @"\bstatic\b") || scope.IsNamespaceScope;
        var returnType = NormalizeCppType(Regex.Replace(prefix, @"\b(static|inline|constexpr|virtual|extern|friend|SONGCORE_EXPORT|[A-Z][A-Z0-9_]+)\b", ""));
        var parameterText = declaration[(paren + 1)..closeParen].Trim();
        var parameters = parameterText.Length == 0 || parameterText == "void" ? Array.Empty<string>() : SplitCppArguments(parameterText).Select(NormalizeCppParameter).ToArray();
        if (parameters.Any(parameter => parameter.Length == 0 || parameter == "..."))
            return false;
        var owner = scope.QualifiedType;
        if (owner.Length == 0)
            return false;
        method = new QuestMethod(name, owner, header, declaration, returnType, parameters, isStatic, scope.BaseTypes.ToArray(), Array.Empty<string>());
        return true;
    }

    private static string NormalizeCppParameter(string parameter)
    {
        var withoutDefault = SplitAtTopLevel(parameter, '=').FirstOrDefault() ?? parameter;
        withoutDefault = withoutDefault.Trim();
        var match = Regex.Match(withoutDefault, @"(?<type>.+?)(?:\s+|\*\s*|&\s*)(?<name>[A-Za-z_]\w*)$");
        return NormalizeCppType(match.Success ? match.Groups["type"].Value : withoutDefault);
    }

    private static string NormalizeCppType(string value) => Regex.Replace(value.Trim(), @"\s+", " ").Replace(" *", "*").Replace(" &", "&").Replace(":: ", "::", StringComparison.Ordinal);

    private static string[] SplitCppArguments(string text)
    {
        var parts = new List<string>();
        var start = 0;
        var angle = 0;
        var round = 0;
        var square = 0;
        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '<':
                    angle++;
                    break;
                case '>':
                    angle = Math.Max(0, angle - 1);
                    break;
                case '(':
                    round++;
                    break;
                case ')':
                    round--;
                    break;
                case '[':
                    square++;
                    break;
                case ']':
                    square--;
                    break;
                case ',' when angle == 0 && round == 0 && square == 0:
                    parts.Add(text[start..index]);
                    start = index + 1;
                    break;
            }
        }
        parts.Add(text[start..]);
        return parts.Select(part => part.Trim()).ToArray();
    }

    private static IEnumerable<string> SplitAtTopLevel(string text, char separator)
    {
        var depth = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is '<' or '(' or '[')
                depth++;
            else if (text[index] is '>' or ')' or ']')
                depth--;
            else if (text[index] == separator && depth == 0)
                return [text[..index], text[(index + 1)..]];
        }
        return [text];
    }

    private static int FindMatching(string value, int start, char open, char close)
    {
        var depth = 0;
        for (var index = start; index < value.Length; index++)
        {
            if (value[index] == open)
                depth++;
            else if (value[index] == close && --depth == 0)
                return index;
        }
        return -1;
    }

    private sealed class CppScopeTracker
    {
        private readonly List<string> _namespaces = [];
        private readonly List<(string Name, string[] Bases)> _types = [];
        private readonly Stack<BraceFrame> _braces = new();
        private string? _pendingNamespace;
        private string? _pendingType;
        private string[] _pendingBases = [];
        public bool IsNamespaceScope => _types.Count == 0;
        public string[] BaseTypes => _types.Count == 0 ? [] : _types[^1].Bases;
        public string QualifiedType
        {
            get
            {
                var parts = _namespaces.SelectMany(item => item.Split("::")).Concat(_types.Select(item => item.Name)).ToArray();
                return string.Join("::", parts);
            }
        }

        public void Observe(string line)
        {
            var namespaceMatch = Regex.Match(line, @"^\s*namespace\s+([\w:]+)\s*(\{)?");
            var classMacro = Regex.Match(line, @"DECLARE_CLASS_CODEGEN(?:_INTERFACES)?\(\s*(?<ns>[\w:]+)\s*,\s*(?<name>\w+)");
            var typeMatch = Regex.Match(line, @"^\s*(?:class|struct)\s+(?<name>\w+)(?:\s*:\s*(?<bases>[^\{]+))?\s*(\{)?");
            if (namespaceMatch.Success)
                _pendingNamespace = namespaceMatch.Groups[1].Value;
            else if (classMacro.Success)
            {
                _pendingType = classMacro.Groups["ns"].Value + "::" + classMacro.Groups["name"].Value;
                _pendingBases = [];
            }
            else if (typeMatch.Success)
            {
                _pendingType = typeMatch.Groups["name"].Value;
                _pendingBases = typeMatch.Groups["bases"].Value.Split(',').Select(value => value.Trim()).Where(value => value.Length > 0).ToArray();
            }

            foreach (var character in line)
            {
                if (character == '{')
                {
                    if (_pendingNamespace is not null)
                    {
                        _namespaces.Add(_pendingNamespace);
                        _braces.Push(new BraceFrame("namespace", _pendingNamespace));
                        _pendingNamespace = null;
                    }
                    else if (_pendingType is not null)
                    {
                        _types.Add((_pendingType, _pendingBases));
                        _braces.Push(new BraceFrame("type", _pendingType));
                        _pendingType = null;
                    }
                    else
                        _braces.Push(new BraceFrame("other", ""));
                }
                else if (character == '}' && _braces.Count > 0)
                {
                    var frame = _braces.Pop();
                    if (frame.Kind == "namespace" && _namespaces.Count > 0)
                        _namespaces.RemoveAt(_namespaces.Count - 1);
                    if (frame.Kind == "type" && _types.Count > 0)
                        _types.RemoveAt(_types.Count - 1);
                }
            }
        }

        private sealed record BraceFrame(string Kind, string Name);
    }
}
