using Mono.Cecil;

namespace Transpiler;

internal sealed record GeneratedModType(string HeaderPath, string Header, string Source, IReadOnlyList<string> Includes);

internal static class GeneratedModTypeEmitter
{
    public static IReadOnlyList<GeneratedModType> Emit(IEnumerable<TypeReference> references, AssemblyDefinition assembly, MappingCatalog catalog, string questIncludeRoot)
    {
        var discovered = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
        var pending = new Queue<TypeReference>(references);
        while (pending.Count > 0)
        {
            var reference = pending.Dequeue();
            if (reference is GenericInstanceType generic)
            {
                foreach (var argument in generic.GenericArguments)
                    pending.Enqueue(argument);
                pending.Enqueue(generic.ElementType);
            }
            else if (reference is TypeSpecification specification)
                pending.Enqueue(specification.ElementType);
            TypeDefinition? resolved;
            try
            {
                resolved = reference.Resolve();
            }
            catch (ResolutionException)
            {
                continue;
            }
            if (resolved is null || resolved.Module.Assembly.Name.Name != assembly.Name.Name || !resolved.IsClass || resolved.IsEnum || resolved.IsAbstract || !discovered.TryAdd(resolved.FullName, resolved))
                continue;
            foreach (var field in resolved.Fields)
                pending.Enqueue(field.FieldType);
            foreach (var method in resolved.Methods.Where(method => method.HasBody))
            {
                pending.Enqueue(method.ReturnType);
                foreach (var parameter in method.Parameters)
                    pending.Enqueue(parameter.ParameterType);
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is TypeReference operandType)
                        pending.Enqueue(operandType);
                    else if (instruction.Operand is FieldReference fieldReference)
                    {
                        pending.Enqueue(fieldReference.DeclaringType);
                        pending.Enqueue(fieldReference.FieldType);
                    }
                    else if (instruction.Operand is MethodReference methodReference)
                    {
                        pending.Enqueue(methodReference.DeclaringType);
                        pending.Enqueue(methodReference.ReturnType);
                        foreach (var parameter in methodReference.Parameters)
                            pending.Enqueue(parameter.ParameterType);
                    }
                }
            }
        }
        var types = discovered.Values.ToArray();
        var results = new List<GeneratedModType>();
        foreach (var type in types)
        {
            var namespaceName = string.IsNullOrWhiteSpace(type.Namespace) ? "GlobalNamespace" : string.Join("::", type.Namespace.Split('.'));
            var cppName = CppTypeName(type);
            var path = TypeHeaderPath(type);
            var baseType = type.BaseType is null ? null : QuestApiTypeMapper.Map(type.BaseType);
            if (baseType is null)
                throw new InvalidOperationException($"Generated mod type {type.FullName} has no Quest mapping for base type {type.BaseType?.FullName ?? "<none>"}.");

            var includes = new HashSet<string>(StringComparer.Ordinal) { "custom-types/shared/register.hpp", "custom-types/shared/macros.hpp" };
            if (type.BaseType is not null)
                AddHeaders(QuestApiTypeMapper.Map(type.BaseType) ?? string.Empty, questIncludeRoot, includes);
            foreach (var field in type.Fields.Where(field => !field.IsStatic && !field.IsLiteral && !field.Name.StartsWith("<", StringComparison.Ordinal)))
            {
                var mapped = QuestApiTypeMapper.Map(field.FieldType) ?? throw new InvalidOperationException($"Generated mod field {field.FullName} has no Quest type representation.");
                AddHeaders(mapped, questIncludeRoot, includes);
                foreach (var localType in LocalTypeReferences(field.FieldType, assembly))
                    includes.Add(TypeHeaderPath(localType));
            }
            foreach (var method in type.Methods.Where(method => !method.IsStatic && method.HasBody && !method.IsConstructor && !method.IsGetter && !method.IsSetter))
            {
                AddHeaders(QuestApiTypeMapper.Map(method.ReturnType) ?? string.Empty, questIncludeRoot, includes);
                foreach (var parameter in method.Parameters)
                    AddHeaders(QuestApiTypeMapper.Map(parameter.ParameterType) ?? string.Empty, questIncludeRoot, includes);
                foreach (var instruction in method.Body.Instructions.Where(instruction => instruction.Operand is MethodReference))
                {
                    var reference = (MethodReference)instruction.Operand;
                    if (reference.DeclaringType.Scope.Name == assembly.Name.Name)
                        continue;
                    if (catalog.ResolveApiCall(reference) is { } mapping)
                    {
                        includes.Add(mapping.PublicHeader);
                        AddHeaders(string.Join(" ", mapping.QuestParameters.Append(mapping.QuestReturn)), questIncludeRoot, includes);
                    }
                }
                foreach (var instruction in method.Body.Instructions)
                    if (instruction.Operand is TypeReference localReference)
                        foreach (var localType in LocalTypeReferences(localReference, assembly))
                            includes.Add(TypeHeaderPath(localType));
                    else if (instruction.Operand is FieldReference localField)
                        foreach (var localType in LocalTypeReferences(localField.DeclaringType, assembly).Concat(LocalTypeReferences(localField.FieldType, assembly)))
                            includes.Add(TypeHeaderPath(localType));
                    else if (instruction.Operand is MethodReference localMethod)
                        foreach (var localType in LocalTypeReferences(localMethod.DeclaringType, assembly).Concat(LocalTypeReferences(localMethod.ReturnType, assembly)).Concat(localMethod.Parameters.SelectMany(parameter => LocalTypeReferences(parameter.ParameterType, assembly))))
                            includes.Add(TypeHeaderPath(localType));
            }

            var declarations = new List<string>();
            var macroTypeAliases = new Dictionary<string, string>(StringComparer.Ordinal);
            string MacroType(string nativeType)
            {
                if (!nativeType.Contains(',', StringComparison.Ordinal))
                    return nativeType;
                if (!macroTypeAliases.TryGetValue(nativeType, out var alias))
                    macroTypeAliases[nativeType] = alias = cppName + "_GeneratedTypeAlias_" + macroTypeAliases.Count;
                return alias;
            }
            foreach (var method in type.Methods.Where(method => !method.IsStatic && method.HasBody && !method.IsConstructor && !method.IsGetter && !method.IsSetter))
            {
                var returnType = QuestApiTypeMapper.Map(method.ReturnType) ?? throw new InvalidOperationException($"Generated mod method {method.FullName} has no Quest return type.");
                var parameters = method.Parameters.Select(
                    (parameter, index) => $"{MacroType(QuestApiTypeMapper.Map(parameter.ParameterType) ?? throw new InvalidOperationException($"Generated mod parameter {parameter.Name} in {method.FullName} has no Quest type."))} {Identifier(parameter.Name, "arg" + index)}"
                );
                declarations.Add($"DECLARE_INSTANCE_METHOD({MacroType(returnType)}, {Identifier(method.Name, "method")}{(method.Parameters.Count == 0 ? "" : ", " + string.Join(", ", parameters))});");
            }
            var mappedFields = type
                .Fields.Where(field => !field.IsStatic && !field.IsLiteral && !field.Name.StartsWith("<", StringComparison.Ordinal))
                .Select(field => (Field: field, Native: field.FieldType.Resolve()?.IsEnum == true ? "int32_t" : QuestApiTypeMapper.Map(field.FieldType) ?? throw new InvalidOperationException($"Generated mod field {field.FullName} has no Quest type representation.")))
                .ToArray();
            foreach (var field in mappedFields)
                _ = MacroType(field.Native);
            var enumTypes = type.NestedTypes.Where(nested => nested.IsEnum).ToArray();
            var enumDeclarations = enumTypes.Select(nested =>
            {
                var enumName = cppName + "_" + nested.Name.Split('`')[0];
                var literals = nested.Fields.Where(field => field.IsLiteral && field.HasConstant).Select(field => $"    {Identifier(field.Name, "value")} = {Convert.ToInt32(field.Constant, System.Globalization.CultureInfo.InvariantCulture)}");
                return $"enum class {enumName} : int32_t {{\n{string.Join(",\n", literals)}\n}};";
            });
            var namespaceDeclarations = enumDeclarations.Concat(macroTypeAliases.Select(alias => $"using {alias.Value} = {alias.Key};")).ToArray();
            var header =
                "#pragma once\n#include <cstdint>\n"
                + string.Join("\n", includes.OrderBy(value => value, StringComparer.Ordinal).Select(include => $"#include \"{include}\""))
                + (namespaceDeclarations.Length == 0 ? "" : $"\n\nnamespace {namespaceName} {{\n{string.Join("\n\n", namespaceDeclarations)}\n}}\n")
                + $"\n\nDECLARE_CLASS_CODEGEN_DLL({namespaceName}, {cppName}, {baseType.TrimEnd('*')}, \"{assembly.Name.Name}\") {{\n    DECLARE_CTOR(__ctor);\n"
                + string.Join("\n", mappedFields.Select(field => $"    DECLARE_INSTANCE_FIELD({MacroType(field.Native)}, ___{Identifier(field.Field.Name, "field")});"))
                + (declarations.Count == 0 ? "" : "\n    " + string.Join("\n    ", declarations))
                + "\n};\n";
            var thunkDeclarations = CecilTypeWalker
                .Flatten(assembly.MainModule.Types)
                .SelectMany(candidateType => candidateType.Methods)
                .Where(candidateMethod => candidateMethod.HasBody)
                .SelectMany(candidateMethod => candidateMethod.Body.Instructions.Where(instruction => instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldftn && instruction.Operand is MethodReference).Select(instruction => ((MethodReference)instruction.Operand).Resolve()))
                .Where(target => target is not null && target.DeclaringType.FullName == type.FullName)
                .Cast<MethodDefinition>()
                .DistinctBy(target => target.MetadataToken)
                .Select(target =>
                    $"{QuestApiTypeMapper.Map(target.ReturnType)} {FunctionThunkName(target)}(void* target{(target.Parameters.Count == 0 ? "" : ", " + string.Join(", ", target.Parameters.Select(parameter => QuestApiTypeMapper.Map(parameter.ParameterType) + " " + Identifier(parameter.Name, "arg"))))});"
                );
            if (thunkDeclarations.Any())
                header += "\n" + string.Join("\n", thunkDeclarations) + "\n";

            var sourceLines = new List<string> { $"#include \"{path}\"", $"DEFINE_TYPE({namespaceName}, {cppName});" };
            var staticDeclarations = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var constructor in type.Methods.Where(method => method.IsConstructor && !method.IsStatic && method.HasBody && method.Parameters.Count == 0))
            {
                var constructorBody = IlMethodTranslator.TranslateModTypeMethod(constructor, catalog);
                foreach (var declaration in constructorBody.SelectMany(line => line.StaticFields ?? Array.Empty<QuestStaticField>()))
                    staticDeclarations[declaration.Name] = declaration.Declaration;
                sourceLines.Add($"void {namespaceName}::{cppName}::__ctor() {{\n{string.Join("\n", constructorBody.Where(line => line.RawCpp is not null).Select(line => "    " + line.RawCpp))}\n}}");
            }
            foreach (var method in type.Methods.Where(method => !method.IsStatic && method.HasBody && !method.IsConstructor && !method.IsGetter && !method.IsSetter))
            {
                var methodBody = IlMethodTranslator.TranslateModTypeMethod(method, catalog);
                foreach (var declaration in methodBody.SelectMany(line => line.StaticFields ?? Array.Empty<QuestStaticField>()))
                    staticDeclarations[declaration.Name] = declaration.Declaration;
                var returnType = QuestApiTypeMapper.Map(method.ReturnType)!;
                var parameters = method.Parameters.Select((parameter, index) => $"{QuestApiTypeMapper.Map(parameter.ParameterType)} {Identifier(parameter.Name, "arg" + index)}");
                sourceLines.Add($"{returnType} {namespaceName}::{cppName}::{Identifier(method.Name, "method")}({string.Join(", ", parameters)}) {{\n{string.Join("\n", methodBody.Where(line => line.RawCpp is not null).Select(line => "    " + line.RawCpp))}\n}}");
            }
            if (staticDeclarations.Count > 0)
                sourceLines.Insert(1, string.Join("\n", staticDeclarations.Values));
            foreach (var candidateMethod in CecilTypeWalker.Flatten(assembly.MainModule.Types).SelectMany(candidateType => candidateType.Methods).Where(candidateMethod => candidateMethod.HasBody))
            foreach (var instruction in candidateMethod.Body.Instructions.Where(instruction => instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldftn && instruction.Operand is MethodReference))
            {
                var target = ((MethodReference)instruction.Operand).Resolve();
                if (target is null || target.DeclaringType.FullName != type.FullName)
                    continue;
                var returnType = QuestApiTypeMapper.Map(target.ReturnType) ?? throw new InvalidOperationException($"Delegate method {target.FullName} has no Quest return representation.");
                var qualifiedType = namespaceName + "::" + cppName;
                var parameters = target
                    .Parameters.Select((parameter, index) => $"{QuestApiTypeMapper.Map(parameter.ParameterType) ?? throw new InvalidOperationException($"Delegate parameter {parameter.Name} in {target.FullName} has no Quest representation.")} {Identifier(parameter.Name, "arg" + index)}")
                    .ToArray();
                var callArguments = string.Join(", ", target.Parameters.Select((parameter, index) => Identifier(parameter.Name, "arg" + index)));
                var call = $"static_cast<{qualifiedType}*>(target)->{MethodName(target)}({callArguments})";
                sourceLines.Add($"{returnType} {FunctionThunkName(target)}(void* target{(parameters.Length == 0 ? "" : ", " + string.Join(", ", parameters))}) {{\n    {(target.ReturnType.MetadataType == MetadataType.Void ? call + ";" : "return " + call + ";")}\n}}");
            }
            results.Add(new GeneratedModType(path, header, string.Join("\n\n", sourceLines) + "\n", includes.ToArray()));
        }
        return results;
    }

    private static void AddHeaders(string mappedType, string questIncludeRoot, ISet<string> includes)
    {
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(mappedType, @"::(?<type>[A-Za-z_]\w*(?:::[A-Za-z_]\w*)*)"))
        {
            var name = match.Groups["type"].Value;
            if (name.StartsWith("GlobalNamespace::", StringComparison.Ordinal))
                continue;
            var relativeHeader = name.Replace("::", "/", StringComparison.Ordinal) + ".hpp";
            if (File.Exists(Path.Combine(questIncludeRoot, relativeHeader.Replace('/', Path.DirectorySeparatorChar))))
                includes.Add(relativeHeader);
        }
    }

    private static IEnumerable<TypeDefinition> LocalTypeReferences(TypeReference type, AssemblyDefinition assembly)
    {
        if (type is GenericInstanceType generic)
        {
            foreach (var argument in generic.GenericArguments)
            foreach (var local in LocalTypeReferences(argument, assembly))
                yield return local;
            type = generic.ElementType;
        }
        else if (type is TypeSpecification specification)
            type = specification.ElementType;
        TypeDefinition? resolved;
        try
        {
            resolved = type.Resolve();
        }
        catch (ResolutionException)
        {
            resolved = null;
        }
        if (resolved is not null && resolved.Module.Assembly.Name.Name == assembly.Name.Name && resolved.IsClass && !resolved.IsEnum)
            yield return resolved;
    }

    private static string TypeHeaderPath(TypeReference type)
    {
        var namespacePath = string.IsNullOrWhiteSpace(type.Namespace) ? "GlobalNamespace" : type.Namespace.Replace('.', '/');
        return namespacePath + "/" + CppTypeName(type) + ".hpp";
    }

    private static string CppTypeName(TypeReference type)
    {
        var names = new Stack<string>();
        for (TypeReference? current = type; current is not null; current = current.DeclaringType)
            names.Push(Identifier(current.Name.Split('`')[0], "GeneratedType"));
        return string.Join("_", names);
    }

    private static string Identifier(string value, string fallback) => System.Text.RegularExpressions.Regex.Replace(value, "[^A-Za-z0-9_]", "_") is { Length: > 0 } name ? name : fallback;

    public static string MethodName(MethodDefinition method) => Identifier(method.Name, "GeneratedMethod");

    public static string FunctionThunkName(MethodDefinition method) => "il2cpp_thunk_" + Identifier(method.DeclaringType.FullName + "_" + method.Name + "_" + method.MetadataToken.ToInt32().ToString("x8", System.Globalization.CultureInfo.InvariantCulture), "GeneratedThunk");
}
