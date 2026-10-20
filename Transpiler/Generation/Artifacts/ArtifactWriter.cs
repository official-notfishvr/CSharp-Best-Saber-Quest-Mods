using System.Collections.Generic;
using System.IO;

namespace Transpiler;

internal sealed partial class Transpiler
{
    private void WriteOutputFile(string outputDirectory, string relativePath, string content)
    {
        var fullPath = Path.Combine(outputDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        GeneratedArtifacts.Add(new GeneratedArtifact { Path = fullPath, Content = content });
    }

    private static void AddInclude(ISet<string> includes, string? include)
    {
        if (!string.IsNullOrWhiteSpace(include))
            includes.Add(include);
    }
}
