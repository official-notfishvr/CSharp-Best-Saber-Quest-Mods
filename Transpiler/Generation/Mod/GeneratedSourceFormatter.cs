using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace Transpiler;

internal sealed partial class Transpiler
{
    private static string NormalizeGeneratedSource(string source)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var marker = " = ::il2cpp_utils::NewSpecific<";
            var markerIndex = lines[i].IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
                continue;

            var lhs = lines[i][..markerIndex].Trim();
            var callStart = lines[i].LastIndexOf('(');
            var callEnd = lines[i].LastIndexOf(");", StringComparison.Ordinal);
            if (callStart < 0 || callEnd < callStart)
                continue;

            var args = lines[i][(callStart + 1)..callEnd].Trim();
            if (string.Equals(args, lhs, StringComparison.Ordinal))
                args = string.Empty;
            var indentLength = lines[i].Length - lines[i].TrimStart().Length;
            var indent = lines[i][..indentLength];
            lines[i] = $"{indent}{lhs} = ::il2cpp_utils::NewSpecific<decltype({lhs})>({args});";
        }

        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = Regex.Replace(lines[i], @"\(\(([^()]+)\)\)", "($1)");
            lines[i] = Regex.Replace(lines[i], @"if \(\(([^()]+)\)\)", "if ($1)");
        }

        var sourceText = string.Join("\n", lines);
        sourceText = SimplifyFunctionLocalTemps(sourceText);

        sourceText = Regex.Replace(sourceText, @"(\r?\n){3,}", Environment.NewLine + Environment.NewLine);
        return sourceText.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    }

    private static string SimplifyFunctionLocalTemps(string sourceText)
    {
        var lines = sourceText.Split('\n').ToList();
        var functionStartRegex = new Regex(@"\)\s*\{\s*$", RegexOptions.Compiled);

        for (var i = 0; i < lines.Count; i++)
        {
            if (!functionStartRegex.IsMatch(lines[i]))
                continue;

            var start = i;
            var depth = CountBraces(lines[i]);
            var end = i;
            while (depth > 0 && end + 1 < lines.Count)
            {
                end++;
                depth += CountBraces(lines[end]);
            }

            var block = string.Join("\n", lines.Skip(start).Take(end - start + 1));
            var simplified = SimplifySingleFunctionBlock(block).Split('\n');
            lines.RemoveRange(start, end - start + 1);
            lines.InsertRange(start, simplified);
            i = start + simplified.Length - 1;
        }

        return string.Join("\n", lines);
    }

    private static string SimplifySingleFunctionBlock(string block)
    {
        var inlineIfRegex = new Regex(@"^(?<assignIndent>\s*)(?<name>local\d+(?:_\d+)?) = (?<expr>.+);\r?\n(?<ifIndent>\s*)if \((?:\()?(?<condition>\k<name>)(?:\))?\)", RegexOptions.Multiline);
        block = inlineIfRegex.Replace(
            block,
            match =>
            {
                var localName = match.Groups["name"].Value;
                var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
                return usageCount == 3 ? $"{match.Groups["ifIndent"].Value}if ({match.Groups["expr"].Value})" : match.Value;
            }
        );

        var duplicateExprIfRegex = new Regex(@"^(?<assignIndent>\s*)(?<name>local\d+(?:_\d+)?) = (?<expr>.+);\r?\n(?<ifIndent>\s*)if \((?:\()?(?<condition>\k<expr>)(?:\))?\)", RegexOptions.Multiline);
        block = duplicateExprIfRegex.Replace(
            block,
            match =>
            {
                var localName = match.Groups["name"].Value;
                var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
                return usageCount == 2 ? $"{match.Groups["ifIndent"].Value}if ({match.Groups["expr"].Value})" : match.Value;
            }
        );

        var wrappedDuplicateExprIfRegex = new Regex(@"^(?<assignIndent>\s*)(?<name>local\d+(?:_\d+)?) = \((?<expr>.+)\);\r?\n(?<ifIndent>\s*)if \((?:\()?(?<condition>\k<expr>)(?:\))?\)", RegexOptions.Multiline);
        block = wrappedDuplicateExprIfRegex.Replace(
            block,
            match =>
            {
                var localName = match.Groups["name"].Value;
                var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
                return usageCount == 2 ? $"{match.Groups["ifIndent"].Value}if ({match.Groups["expr"].Value})" : match.Value;
            }
        );

        var inlineReturnRegex = new Regex(@"^(?<assignIndent>\s*)(?<name>local\d+(?:_\d+)?) = (?<expr>.+);\r?\n(?<returnIndent>\s*)return (?<returnName>\k<name>);", RegexOptions.Multiline);
        block = inlineReturnRegex.Replace(
            block,
            match =>
            {
                var localName = match.Groups["name"].Value;
                var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
                return usageCount == 3 ? $"{match.Groups["returnIndent"].Value}return {match.Groups["expr"].Value};" : match.Value;
            }
        );

        var localAssignmentRegex = new Regex(@"^(?<indent>\s*)(?<name>local\d+(?:_\d+)?) = (?<expr>.+);\s*$", RegexOptions.Multiline);
        var changed = true;
        while (changed)
        {
            changed = false;
            block = localAssignmentRegex.Replace(
                block,
                match =>
                {
                    var localName = match.Groups["name"].Value;
                    var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
                    if (usageCount == 2)
                    {
                        changed = true;
                        return string.Empty;
                    }

                    return match.Value;
                }
            );
        }

        var localDeclarationRegex = new Regex(@"^(?<indent>\s*)(?<type>[\w:<>]+(?:\s*[*&])?)\s+(?<name>local\d+(?:_\d+)?)\{\};\s*$", RegexOptions.Multiline);
        block = localDeclarationRegex.Replace(
            block,
            match =>
            {
                var localName = match.Groups["name"].Value;
                var usageCount = Regex.Matches(block, $@"\b{Regex.Escape(localName)}\b").Count;
                return usageCount <= 1 ? string.Empty : match.Value;
            }
        );

        return block;
    }

    private static int CountBraces(string line)
    {
        var depth = 0;
        foreach (var ch in line)
        {
            if (ch == '{')
                depth++;
            else if (ch == '}')
                depth--;
        }

        return depth;
    }
}
