using Mono.Cecil;

namespace Transpiler;

internal static class IlInterfaceConversion
{
    public static string ConvertArgument(IlTranslationContext context, QuestValue argument, TypeReference expectedType, string nativeExpectedType)
    {
        TypeDefinition? expectedDefinition;
        try
        {
            expectedDefinition = (expectedType is GenericInstanceType generic ? generic.ElementType : expectedType).Resolve();
        }
        catch (ResolutionException)
        {
            expectedDefinition = null;
        }
        if (expectedDefinition?.IsInterface != true)
            return AsCppValue(argument);

        var sourceType = context.FindKnownType(argument.PcType);
        if (sourceType is ArrayType)
        {
            return $"reinterpret_cast<{nativeExpectedType}>(static_cast<Il2CppArray*>({argument.Code}))";
        }

        if (sourceType is not null && TryConvert(sourceType, expectedType, argument.Code, out var converted))
            return converted;
        return AsCppValue(argument);
    }

    public static bool TryConvert(TypeReference sourceType, TypeReference targetInterface, string expression, out string converted, string? requiredMethod = null)
    {
        converted = expression;
        TypeDefinition? sourceDefinition;
        try
        {
            sourceDefinition = (sourceType is GenericInstanceType generic ? generic.ElementType : sourceType).Resolve();
        }
        catch (ResolutionException)
        {
            sourceDefinition = null;
        }
        if (sourceDefinition?.Methods.Any(method => method.Name == requiredMethod) == true)
            return false;
        var path = FindPath(sourceType, targetInterface, new HashSet<string>(StringComparer.Ordinal));
        if (path is null || path.Count == 0)
            return false;
        foreach (var method in path)
            converted += "->" + method + "()";
        return true;
    }

    private static IReadOnlyList<string>? FindPath(TypeReference sourceType, TypeReference targetInterface, ISet<string> visited)
    {
        if (!visited.Add(sourceType.FullName))
            return null;
        TypeDefinition? definition;
        try
        {
            definition = (sourceType is GenericInstanceType generic ? generic.ElementType : sourceType).Resolve();
        }
        catch (ResolutionException)
        {
            return null;
        }
        if (definition is null)
            return null;

        var closedSource = sourceType as GenericInstanceType;
        foreach (var implementation in definition.Interfaces)
        {
            var projection = Substitute(implementation.InterfaceType, closedSource);
            if (projection.FullName == targetInterface.FullName)
                return [ConversionMethodName(implementation.InterfaceType)];
            var nestedPath = FindPath(projection, targetInterface, visited);
            if (nestedPath is not null)
                return new[] { ConversionMethodName(implementation.InterfaceType) }.Concat(nestedPath).ToArray();
        }

        if (definition.BaseType is not null)
        {
            var baseType = Substitute(definition.BaseType, sourceType as GenericInstanceType);
            var basePath = FindPath(baseType, targetInterface, visited);
            if (basePath is not null)
                return basePath;
        }

        return null;
    }

    private static TypeReference Substitute(TypeReference type, GenericInstanceType? closedType)
    {
        if (type is GenericParameter parameter && parameter.Type == GenericParameterType.Type && closedType is not null && parameter.Position < closedType.GenericArguments.Count)
            return closedType.GenericArguments[parameter.Position];
        if (type is GenericInstanceType generic)
        {
            var result = new GenericInstanceType(generic.ElementType);
            foreach (var argument in generic.GenericArguments)
                result.GenericArguments.Add(Substitute(argument, closedType));
            return result;
        }
        return type;
    }

    private static string ConversionMethodName(TypeReference interfaceType)
    {
        var definition = interfaceType is GenericInstanceType generic ? generic.ElementType : interfaceType;
        var namespacePrefix = string.IsNullOrWhiteSpace(definition.Namespace) ? string.Empty : definition.Namespace.Replace(".", "__", StringComparison.Ordinal) + "__";
        var name = definition.Name;
        var tick = name.IndexOf('`');
        if (tick < 0)
            return "i___" + namespacePrefix + name;

        var arityText = name[(tick + 1)..];
        var arity = int.TryParse(arityText, out var parsed) ? parsed : 0;
        var arguments = interfaceType is GenericInstanceType closed ? closed.GenericArguments.Select(argument => Identifier(argument.Name)) : Enumerable.Range(0, arity).Select(index => "T" + index);
        return "i___" + namespacePrefix + name[..tick] + "_" + arity + "_" + string.Join("_", arguments) + "_";
    }

    private static string Identifier(string value) => System.Text.RegularExpressions.Regex.Replace(value, "[^A-Za-z0-9_]", "_");

    private static string AsCppValue(QuestValue value) =>
        value.PcType == "System.String" && value.IsStringLiteral
            ? "StringW(\"" + value.Code.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\")"
            : value.Code;
}
