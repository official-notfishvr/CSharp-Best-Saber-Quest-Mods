using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed partial class Transpiler
{
    private readonly string _assemblyPath;
    private readonly List<PatchDefinition> _patches = new();
    private readonly List<ConfigEntry> _configValues = new();
    private readonly List<LocalStaticFieldEntry> _localStaticFields = new();
    private readonly List<CustomTypeEntry> _customTypes = new();
    private readonly List<MethodDefinition> _helperMethods = new();
    private readonly List<MenuButtonRegistration> _menuButtons = new();
    private readonly List<GameplaySetupTabRegistration> _gameplaySetupTabs = new();
    private readonly CppTypeSystem _typeSystem = new();
    private readonly TypeMetadataIndex _metadataIndex;
    private ModuleDefinition? _module;
    private ModMetadata _modMetadata = new();

    public Transpiler(string assemblyPath)
    {
        _assemblyPath = assemblyPath;
        _metadataIndex = TypeMetadataIndex.Load(assemblyPath);
    }

    public List<GeneratedArtifact> GeneratedArtifacts { get; } = new();

    public void Load()
    {
        var resolver = new DefaultAssemblyResolver();
        var assemblyDirectory = Path.GetDirectoryName(_assemblyPath) ?? Directory.GetCurrentDirectory();
        resolver.AddSearchDirectory(assemblyDirectory);

        var readerParameters = new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadSymbols = File.Exists(Path.ChangeExtension(_assemblyPath, ".pdb")),
            InMemory = true,
        };

        _module = ModuleDefinition.ReadModule(_assemblyPath, readerParameters);

        foreach (var type in _module.Types)
            ProcessType(type);

        ValidateHookTargetSignatures();
        AssignConfigIdentifiers();
    }

    public void GenerateOutput(string outputDirectory)
    {
        if (_module == null)
            throw new InvalidOperationException("Load must be called before GenerateOutput.");

        Directory.CreateDirectory(outputDirectory);

        WriteOutputFile(outputDirectory, Path.Combine("include", "_config.hpp"), ConfigGenerator.GenerateHeader(_configValues, _typeSystem));
        GenerateMainHeader(outputDirectory);
        GenerateMainSource(outputDirectory);
    }
}
