using System;
using System.IO;

namespace Transpiler;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (!CommandLineOptions.TryParse(args, out var options, out var error))
        {
            // Transpiler.exe --dir C:\Users\Administrator\Downloads\Files\fit\SampleMod C:\Users\Administrator\Downloads\Files\fit\quest-mod
            Console.Error.WriteLine(error);
            PrintUsage();
            return 1;
        }

        try
        {
            var assemblyPath = AssemblyLocator.ResolveAssemblyPath(options.InputPath);
            Console.WriteLine($"Using assembly: {assemblyPath}");

            var transpiler = new Transpiler(assemblyPath);
            transpiler.Load();
            transpiler.GenerateOutput(Path.GetFullPath(options.OutputDirectory));

            Console.WriteLine($"Generated {transpiler.GeneratedArtifacts.Count} file(s) in {Path.GetFullPath(options.OutputDirectory)}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: Transpiler <input.dll|input.csproj|input_dir> [output_dir]");
        Console.WriteLine("       Transpiler --dir <input_dir> [output_dir]");
        Console.WriteLine("Note: projects/directories must already be built; Transpiler no longer invokes dotnet build.");
    }
}

internal sealed class CommandLineOptions
{
    public required string InputPath { get; init; }
    public required string OutputDirectory { get; init; }

    public static bool TryParse(string[] args, out CommandLineOptions options, out string? error)
    {
        options = null!;
        error = null;

        if (args.Length == 0)
        {
            error = "Missing input path.";
            return false;
        }

        string inputPath;
        string outputDirectory;

        if (string.Equals(args[0], "--dir", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2)
            {
                error = "Missing input directory for --dir.";
                return false;
            }

            inputPath = args[1];
            outputDirectory = args.Length > 2 ? args[2] : "output";
        }
        else
        {
            inputPath = args[0];
            outputDirectory = args.Length > 1 ? args[1] : "output";
        }

        options = new CommandLineOptions { InputPath = inputPath, OutputDirectory = outputDirectory };

        return true;
    }
}
