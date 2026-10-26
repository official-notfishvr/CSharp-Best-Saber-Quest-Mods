using Mono.Cecil;

namespace Transpiler;

internal static class CecilTypeWalker
{
    public static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Flatten(type.NestedTypes))
                yield return nested;
        }
    }
}
