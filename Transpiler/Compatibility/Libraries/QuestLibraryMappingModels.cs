using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace Transpiler;

internal static partial class QuestLibraryMappingGenerator
{
    private sealed record QpmPackage(string Id, string Version, string SourceRoot, bool HeadersOnly);

    private sealed record StructuralAssociation(string PcAssembly, QpmPackage Package, string MethodKey, string PcType, string PcMethod, string QuestType, string QuestMethod, string QuestHeader, string Resolution);

    private sealed record StructuralTypeAssociation(string PcAssembly, string PcType, QpmPackage Package, string[] QuestTypes, string[] Headers, int CandidateCount);

    private sealed record PcMethodSignature(string PcAssembly, string PcType, string PcMethod, int GenericArity, TypeReference ReturnType, TypeReference[] Parameters, bool IsStatic)
    {
        public string Key => $"{PcAssembly}|{PcType}|{PcMethod}|{GenericArity}|{ReturnType.FullName}|{IsStatic}|{string.Join(";", Parameters.Select(parameter => parameter.FullName))}";

        public static PcMethodSignature FromDefinition(MethodDefinition method, string assemblyName) =>
            new(assemblyName, method.DeclaringType.FullName, method.Name, method.GenericParameters.Count, method.ReturnType, method.Parameters.Select(parameter => parameter.ParameterType).ToArray(), method.IsStatic);

        public static PcMethodSignature FromReference(MethodReference method, string assemblyName) =>
            new(assemblyName, method.DeclaringType.FullName, method.Name, method.GenericParameters.Count, method.ReturnType, method.Parameters.Select(parameter => parameter.ParameterType).ToArray(), !method.HasThis);
    }

    private sealed record QuestMethod(string Name, string Owner, string Header, string Declaration, string ReturnType, string[] Parameters, bool IsStatic, string[] BaseTypes, string[] AlternateHeaders)
    {
        public string TargetKey => $"{Owner}|{Name}|{ReturnType}|{string.Join(";", Parameters)}|{IsStatic}";
    }

    private sealed record Inference(QuestMethod Method, int Score, string[] ArgumentConversions, string? ReturnConversion, int NameScore);

    private sealed class Unsupported
    {
        public static readonly Unsupported Value = new();

        private Unsupported() { }
    }
}
