using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsToCppTranspiler;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: CsToCppTranspiler <input.cs> [output_dir]");
            Console.WriteLine("       CsToCppTranspiler --dir <input_dir> [output_dir]");
            return 1;
        }

        string outputDir = args.Length > 1 ? args[1] : "output";
        
        try
        {
            if (args[0] == "--dir")
            {
                string inputDir = args.Length > 1 ? args[1] : ".";
                outputDir = args.Length > 2 ? args[2] : "output";
                ProcessDirectory(inputDir, outputDir);
            }
            else
            {
                string inputFile = args[0];
                ProcessFile(inputFile, outputDir);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static void ProcessDirectory(string inputDir, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        
        var csFiles = Directory.GetFiles(inputDir, "*.cs", SearchOption.AllDirectories);
        var transpiler = new Transpiler();
        
        foreach (var file in csFiles)
        {
            Console.WriteLine($"Processing: {file}");
            var code = File.ReadAllText(file);
            transpiler.AddSource(code, file);
        }
        
        transpiler.Compile();
        transpiler.GenerateOutput(outputDir);
        
        Console.WriteLine($"Generated {transpiler.GeneratedFiles.Count} file(s) in {outputDir}");
    }

    static void ProcessFile(string inputFile, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        
        var code = File.ReadAllText(inputFile);
        var transpiler = new Transpiler();
        transpiler.AddSource(code, inputFile);
        transpiler.Compile();
        transpiler.GenerateOutput(outputDir);
        
        Console.WriteLine($"Generated {transpiler.GeneratedFiles.Count} file(s) in {outputDir}");
    }
}

class Transpiler
{
    private List<(string Source, string Path)> _sources = new();
    private CSharpCompilation? _compilation;
    public List<GeneratedFile> GeneratedFiles { get; } = new();
    private TypeMapper _typeMapper = new();
    private List<HookInfo> _hooks = new();
    private List<ClassInfo> _classes = new();
    private List<ConfigValueInfo> _configValues = new();
    private HashSet<string> _usedTypes = new();
    private Dictionary<(string Class, string Field), string> _fieldTypes = new();
    private Dictionary<(string Class, string Method), string> _methodReturnTypes = new();
    private ModInfo _modInfo = new() { Id = "mod", Version = "1.0.0" };

    public void AddSource(string code, string path)
    {
        _sources.Add((code, path));
    }

    public void Compile()
    {
        var syntaxTrees = _sources.Select(s => CSharpSyntaxTree.ParseText(s.Source, path: s.Path)).ToList();
        
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithAllowUnsafe(true);
        
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
        };

        _compilation = CSharpCompilation.Create("TempAssembly", syntaxTrees, references, options);
        
        foreach (var tree in syntaxTrees)
        {
            ProcessSyntaxTree(tree);
        }
    }

    private void ProcessSyntaxTree(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        
        foreach (var node in root.DescendantNodes())
        {
            if (node is ClassDeclarationSyntax classDecl)
            {
                ProcessClass(classDecl);
            }
        }
    }

    private void ProcessClass(ClassDeclarationSyntax classDecl)
    {
        var classInfo = new ClassInfo
        {
            Name = classDecl.Identifier.Text,
            Namespace = GetNamespace(classDecl),
            IsStatic = classDecl.Modifiers.Any(m => m.Text == "static"),
            Methods = new List<MethodInfo>(),
            Fields = new List<FieldInfo>(),
            Properties = new List<PropertyInfo>()
        };

        var modAttr = classDecl.AttributeLists
            .SelectMany(a => a.Attributes)
            .FirstOrDefault(a => a.Name.ToString() == "Mod" || a.Name.ToString() == "ModAttribute");

        if (modAttr?.ArgumentList?.Arguments.Count >= 2)
        {
            var idArg = modAttr.ArgumentList.Arguments[0];
            var versionArg = modAttr.ArgumentList.Arguments[1];
            
            if (idArg.Expression is LiteralExpressionSyntax idLiteral)
                _modInfo.Id = idLiteral.Token.ValueText;
            if (versionArg.Expression is LiteralExpressionSyntax versionLiteral)
                _modInfo.Version = versionLiteral.Token.ValueText;
        }

        foreach (var member in classDecl.Members)
        {
            if (member is MethodDeclarationSyntax methodDecl)
            {
                ProcessMethod(methodDecl, classInfo);
                var methodName = methodDecl.Identifier.Text;
                var returnType = methodDecl.ReturnType.ToString();
                if (returnType != "void" && returnType != "default")
                {
                    _methodReturnTypes[(classInfo.Name, methodName)] = returnType;
                }
            }
            else if (member is FieldDeclarationSyntax fieldDecl)
            {
                var fieldType = fieldDecl.Declaration.Type.ToString();
                foreach (var variable in fieldDecl.Declaration.Variables)
                {
                    var fieldName = variable.Identifier.Text;
                    classInfo.Fields.Add(new FieldInfo
                    {
                        Name = fieldName,
                        Type = fieldType
                    });
                    _fieldTypes[(classInfo.Name, fieldName)] = fieldType;
                }
            }
            else if (member is PropertyDeclarationSyntax propDecl)
            {
                var hasConfigAttr = propDecl.AttributeLists
                    .SelectMany(a => a.Attributes)
                    .Any(a => a.Name.ToString() == "Config" || a.Name.ToString() == "ConfigAttribute");

                if (hasConfigAttr && classInfo.IsStatic)
                {
                    var configAttr = propDecl.AttributeLists
                        .SelectMany(a => a.Attributes)
                        .FirstOrDefault(a => a.Name.ToString() == "Config" || a.Name.ToString() == "ConfigAttribute");

                    var configInfo = new ConfigValueInfo
                    {
                        Name = propDecl.Identifier.Text,
                        Type = propDecl.Type.ToString(),
                        Description = ""
                    };

                    if (propDecl.Initializer != null)
                    {
                        configInfo.DefaultValue = propDecl.Initializer.Value.ToString();
                    }

                    _configValues.Add(configInfo);
                }

                classInfo.Properties.Add(new PropertyInfo
                {
                    Name = propDecl.Identifier.Text,
                    Type = propDecl.Type.ToString(),
                    HasGetter = propDecl.AccessorList?.Accessors.Any(a => a.Keyword.Text == "get") ?? false,
                    HasSetter = propDecl.AccessorList?.Accessors.Any(a => a.Keyword.Text == "set") ?? false
                });
            }
        }

        _classes.Add(classInfo);
    }

    private string GetNamespace(SyntaxNode node)
    {
        var nsDecl = node.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
        return nsDecl?.Name.ToString() ?? "";
    }

    private string GetNamespaceForType(string typeName)
    {
        var knownNamespaces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "StandardLevelDetailViewController", "GlobalNamespace" },
            { "StandardLevelDetailView", "GlobalNamespace" },
            { "GameplayCoreSceneSetup", "GlobalNamespace" },
            { "CurvedTextMeshPro", "HMUI" },
            { "Button", "UnityEngine::UI" },
            { "GameObject", "UnityEngine" },
            { "Transform", "UnityEngine" },
            { "Component", "UnityEngine" },
            { "RectTransform", "UnityEngine" },
        };
        
        return knownNamespaces.TryGetValue(typeName, out var ns) ? ns : "GlobalNamespace";
    }

    private void ProcessMethod(MethodDeclarationSyntax methodDecl, ClassInfo? classInfo = null)
    {
        var hasHookAttr = methodDecl.AttributeLists
            .SelectMany(a => a.Attributes)
            .Any(a => a.Name.ToString() == "Hook" || a.Name.ToString() == "HookAttribute");

        var methodInfo = new MethodInfo
        {
            Name = methodDecl.Identifier.Text,
            ReturnType = methodDecl.ReturnType.ToString(),
            Parameters = methodDecl.ParameterList.Parameters
                .Select(p => new ParameterInfo
                {
                    Name = p.Identifier.Text,
                    Type = p.Type?.ToString() ?? ""
                }).ToList(),
            IsStatic = methodDecl.Modifiers.Any(m => m.Text == "static"),
            IsHook = hasHookAttr,
            Body = methodDecl.Body
        };

        classInfo?.Methods.Add(methodInfo);

        if (hasHookAttr)
        {
            var hookAttr = methodDecl.AttributeLists
                .SelectMany(a => a.Attributes)
                .FirstOrDefault(a => a.Name.ToString() == "Hook" || a.Name.ToString() == "HookAttribute");

            var targetClass = "";
            var targetMethod = methodDecl.Identifier.Text;
            
            if (hookAttr?.ArgumentList?.Arguments.Count > 0)
            {
                foreach (var arg in hookAttr.ArgumentList.Arguments)
                {
                    var value = arg.Expression is LiteralExpressionSyntax literal ? literal.Token.ValueText : "";
                    
                    if (arg.NameColon != null)
                    {
                        var name = arg.NameColon.Name.Identifier.Text;
                        if (name == "ClassName")
                            targetClass = value;
                        else if (name == "MethodName")
                            targetMethod = value;
                    }
                    else if (arg.NameEquals != null)
                    {
                        var name = arg.NameEquals.Name.Identifier.Text;
                        if (name == "ClassName")
                            targetClass = value;
                    }
                    else
                    {
                        targetMethod = value;
                    }
                }
            }

            _hooks.Add(new HookInfo
            {
                HookName = methodDecl.Identifier.Text + "Hook",
                TargetClass = targetClass,
                TargetMethod = targetMethod,
                Method = methodInfo
            });
        }
    }

    public void GenerateOutput(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        
        GenerateConfigHpp(outputDir);
        GenerateMainCpp(outputDir);
        GenerateMainHpp(outputDir);
        
        foreach (var classInfo in _classes.Where(c => !c.IsStatic))
        {
            GenerateClassFiles(classInfo, outputDir);
        }
    }

    private void GenerateConfigHpp(string outputDir)
    {
        var sb = new CodeBuilder();
        
        sb.AppendLine("#pragma once");
        sb.AppendLine();
        sb.AppendLine("#define MOD_EXPORT __attribute__((visibility(\"default\")))");
        sb.AppendLine("#define MOD_EXTERN_FUNC extern \"C\" MOD_EXPORT");
        sb.AppendLine();
        sb.AppendLine("#include \"beatsaber-hook/shared/utils/il2cpp-utils.hpp\"");
        sb.AppendLine();
        sb.AppendLine($"#define MOD_ID \"{_modInfo.Id}\"");
        sb.AppendLine($"#define VERSION \"{_modInfo.Version}\"");
        sb.AppendLine();

        foreach (var config in _configValues)
        {
            var cppType = _typeMapper.MapType(config.Type);
            var defaultValue = config.DefaultValue;
            
            if (config.Type == "string")
            {
                sb.AppendLine($"static {cppType} {config.Name} = il2cpp_utils::newcsstr(\"{defaultValue.Trim('"')}\");");
            }
            else if (config.Type == "bool")
            {
                sb.AppendLine($"static {cppType} {config.Name} = {defaultValue.ToLower()};");
            }
            else
            {
                sb.AppendLine($"static {cppType} {config.Name} = {defaultValue};");
            }
        }

        var filePath = Path.Combine(outputDir, "include", "_config.hpp");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, sb.ToString());
        GeneratedFiles.Add(new GeneratedFile { Path = filePath, Content = sb.ToString() });
    }

    private void GenerateMainCpp(string outputDir)
    {
        foreach (var hook in _hooks)
        {
            if (hook.Method.Body != null)
            {
                var bodyGenerator = new StatementGenerator(_typeMapper, hook, t => _usedTypes.Add(t), GetNamespaceForType, _fieldTypes, _methodReturnTypes);
                bodyGenerator.Generate(hook.Method.Body);
            }
        }
        
        var sb = new CodeBuilder();
        
        sb.AppendLine("#include \"main.hpp\"");
        sb.AppendLine("#include \"scotland2/shared/modloader.h\"");
        sb.AppendLine();

        var includedClasses = new HashSet<string>();
        foreach (var hook in _hooks)
        {
            if (!string.IsNullOrEmpty(hook.TargetClass) && includedClasses.Add(hook.TargetClass))
            {
                sb.AppendLine($"#include \"GlobalNamespace/{hook.TargetClass}.hpp\"");
            }
        }
        
        foreach (var usedType in _usedTypes)
        {
            if (usedType == "T" || usedType.Length <= 1)
                continue;
                
            var ns = GetNamespaceForType(usedType);
            var includePath = ns.Replace("::", "/");
            if (includedClasses.Add(usedType))
            {
                sb.AppendLine($"#include \"{includePath}/{usedType}.hpp\"");
            }
        }
        sb.AppendLine();

        sb.AppendLine($"static modloader::ModInfo modInfo{{\"{_modInfo.Id}\", \"{_modInfo.Version}\", 0}};");
        sb.AppendLine();

        sb.AppendLine("Configuration &getConfig() {");
        sb.AppendLine("    static Configuration config(modInfo);");
        sb.AppendLine("    return config;");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("MOD_EXTERN_FUNC void setup(CModInfo *info) noexcept {");
        sb.AppendLine("    *info = modInfo.to_c();");
        sb.AppendLine("    getConfig().Load();");
        sb.AppendLine("    Paper::Logger::RegisterFileContextId(PaperLogger.tag);");
        sb.AppendLine("    PaperLogger.info(\"Completed setup!\");");
        sb.AppendLine("}");
        sb.AppendLine();

        foreach (var hook in _hooks)
        {
            GenerateHook(sb, hook);
        }

        sb.AppendLine("MOD_EXTERN_FUNC void late_load() noexcept {");
        sb.AppendLine("    il2cpp_functions::Init();");
        sb.AppendLine("    PaperLogger.info(\"Installing hooks...\");");
        sb.AppendLine();

        foreach (var hook in _hooks)
        {
            sb.AppendLine($"    INSTALL_HOOK(PaperLogger, {hook.HookName});");
        }

        sb.AppendLine();
        sb.AppendLine("    PaperLogger.info(\"Installed all hooks!\");");
        sb.AppendLine("}");

        var filePath = Path.Combine(outputDir, "src", "main.cpp");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, sb.ToString());
        GeneratedFiles.Add(new GeneratedFile { Path = filePath, Content = sb.ToString() });
    }

    private void GenerateHook(CodeBuilder sb, HookInfo hook)
    {
        var returnType = _typeMapper.MapType(hook.Method.ReturnType);
        var args = new List<string>();
        
        foreach (var param in hook.Method.Parameters)
        {
            var paramType = _typeMapper.MapType(param.Type);
            if (paramType == $"{hook.TargetClass}*")
            {
                paramType = $"GlobalNamespace::{hook.TargetClass}*";
            }
            args.Add($"{paramType} {param.Name}");
        }

        sb.AppendLine($"MAKE_HOOK_MATCH(");
        sb.AppendLine($"    {hook.HookName},");
        sb.AppendLine($"    &GlobalNamespace::{hook.TargetClass}::{hook.TargetMethod},");
        sb.AppendLine($"    {returnType},");
        sb.AppendLine($"    {string.Join(", ", args)}) {{");
        sb.AppendLine();

        if (hook.Method.Body != null)
        {
            var bodyGenerator = new StatementGenerator(_typeMapper, hook, t => _usedTypes.Add(t), GetNamespaceForType, _fieldTypes, _methodReturnTypes);
            var generatedBody = bodyGenerator.Generate(hook.Method.Body);
            foreach (var line in generatedBody)
            {
                sb.AppendLine($"    {line}");
            }
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    private void GenerateMainHpp(string outputDir)
    {
        var sb = new CodeBuilder();
        
        sb.AppendLine("#pragma once");
        sb.AppendLine();
        sb.AppendLine("#include \"scotland2/shared/modloader.h\"");
        sb.AppendLine("#include \"beatsaber-hook/shared/config/config-utils.hpp\"");
        sb.AppendLine("#include \"beatsaber-hook/shared/utils/hooking.hpp\"");
        sb.AppendLine("#include \"beatsaber-hook/shared/utils/il2cpp-functions.hpp\"");
        sb.AppendLine("#include \"beatsaber-hook/shared/utils/logging.hpp\"");
        sb.AppendLine("#include \"paper2_scotland2/shared/logger.hpp\"");
        sb.AppendLine("#include \"_config.hpp\"");
        sb.AppendLine();
        sb.AppendLine("Configuration &getConfig();");
        sb.AppendLine();
        sb.AppendLine($"constexpr auto PaperLogger = Paper::ConstLoggerContext(\"{_modInfo.Id}\");");

        var filePath = Path.Combine(outputDir, "include", "main.hpp");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, sb.ToString());
        GeneratedFiles.Add(new GeneratedFile { Path = filePath, Content = sb.ToString() });
    }

    private void GenerateClassFiles(ClassInfo classInfo, string outputDir)
    {
        var hppPath = Path.Combine(outputDir, "include", $"{classInfo.Name}.hpp");
        Directory.CreateDirectory(Path.GetDirectoryName(hppPath)!);
        
        var sb = new CodeBuilder();
        sb.AppendLine("#pragma once");
        sb.AppendLine();
        
        foreach (var field in classInfo.Fields)
        {
            sb.AppendLine($"// Field: {field.Type} {field.Name}");
        }
        
        foreach (var method in classInfo.Methods)
        {
            sb.AppendLine($"// Method: {method.ReturnType} {method.Name}({string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"))})");
        }

        File.WriteAllText(hppPath, sb.ToString());
        GeneratedFiles.Add(new GeneratedFile { Path = hppPath, Content = sb.ToString() });
    }
}

class CodeBuilder
{
    private List<string> _lines = new();

    public void AppendLine(string line = "")
    {
        _lines.Add(line);
    }

    public override string ToString()
    {
        return string.Join("\n", _lines);
    }
}

class TypeMapper
{
    private Dictionary<string, string> _typeMap = new()
    {
        { "void", "void" },
        { "int", "int32_t" },
        { "long", "int64_t" },
        { "float", "float" },
        { "double", "double" },
        { "bool", "bool" },
        { "string", "Il2CppString*" },
        { "object", "Il2CppObject*" },
        { "byte", "uint8_t" },
        { "sbyte", "int8_t" },
        { "short", "int16_t" },
        { "ushort", "uint16_t" },
        { "uint", "uint32_t" },
        { "ulong", "uint64_t" },
        { "char", "Il2CppChar" },
        { "IntPtr", "void*" },
        { "UIntPtr", "void*" },
    };

    public string MapType(string csharpType)
    {
        if (_typeMap.TryGetValue(csharpType, out var cppType))
        {
            return cppType;
        }

        if (csharpType.EndsWith("[]"))
        {
            var elementType = csharpType.TrimEnd('[', ']');
            return $"Il2CppArray<{MapType(elementType)}>*";
        }

        if (csharpType.StartsWith("List<") || csharpType.StartsWith("IEnumerable<"))
        {
            return "Il2CppObject*";
        }

        if (csharpType.StartsWith("Action<") || csharpType.StartsWith("Func<"))
        {
            return "Il2CppDelegate*";
        }

        return $"{csharpType}*";
    }
}

class StatementGenerator
{
    private TypeMapper _typeMapper;
    private HookInfo _hook;
    private Action<string> _addUsedType;
    private Func<string, string> _getNamespaceForType;
    private Dictionary<(string Class, string Field), string> _fieldTypes;
    private Dictionary<(string Class, string Method), string> _methodReturnTypes;
    private Dictionary<string, string> _varTypes = new();

    public StatementGenerator(TypeMapper typeMapper, HookInfo hook, Action<string> addUsedType, Func<string, string> getNamespaceForType, Dictionary<(string Class, string Field), string> fieldTypes, Dictionary<(string Class, string Method), string> methodReturnTypes)
    {
        _typeMapper = typeMapper;
        _hook = hook;
        _addUsedType = addUsedType;
        _getNamespaceForType = getNamespaceForType;
        _fieldTypes = fieldTypes;
        _methodReturnTypes = methodReturnTypes;
        
        if (hook.Method.Parameters.Count > 0)
        {
            var selfParam = hook.Method.Parameters[0];
            if (selfParam.Name == "self")
            {
                _varTypes["self"] = hook.TargetClass;
            }
        }
    }

    public List<string> Generate(BlockSyntax? body)
    {
        var lines = new List<string>();
        
        if (body == null) return lines;

        foreach (var statement in body.Statements)
        {
            lines.AddRange(GenerateStatement(statement));
        }

        return lines;
    }

    private List<string> GenerateStatement(StatementSyntax statement)
    {
        var lines = new List<string>();

        switch (statement)
        {
            case LocalDeclarationStatementSyntax localDecl:
                lines.AddRange(GenerateLocalDeclaration(localDecl));
                break;
            case ExpressionStatementSyntax exprStmt:
                lines.Add(GenerateExpression(exprStmt.Expression) + ";");
                break;
            case ReturnStatementSyntax returnStmt:
                var returnValue = returnStmt.Expression != null 
                    ? GenerateExpression(returnStmt.Expression) 
                    : "";
                lines.Add($"return {returnValue};");
                break;
            case IfStatementSyntax ifStmt:
                lines.AddRange(GenerateIfStatement(ifStmt));
                break;
            case ForEachStatementSyntax forEachStmt:
                lines.AddRange(GenerateForEachStatement(forEachStmt));
                break;
            case WhileStatementSyntax whileStmt:
                lines.AddRange(GenerateWhileStatement(whileStmt));
                break;
            default:
                lines.Add($"// TODO: {statement.GetType().Name}");
                break;
        }

        return lines;
    }

    private List<string> GenerateLocalDeclaration(LocalDeclarationStatementSyntax localDecl)
    {
        var lines = new List<string>();
        var typeStr = localDecl.Declaration.Type.ToString();
        var type = typeStr == "var" ? "auto" : _typeMapper.MapType(typeStr);
        
        foreach (var variable in localDecl.Declaration.Variables)
        {
            var name = variable.Identifier.Text;
            if (variable.Initializer != null)
            {
                var init = GenerateExpression(variable.Initializer.Value);
                lines.Add($"{type} {name} = {init};");
                
                if (typeStr == "var" && variable.Initializer.Value is InvocationExpressionSyntax invocation)
                {
                    if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                    {
                        var objExpr = memberAccess.Expression;
                        var methodName = memberAccess.Name.Identifier.Text;
                        
                        if (objExpr is IdentifierNameSyntax idSyntax)
                        {
                            var objVarName = idSyntax.Identifier.Text;
                            if (_varTypes.TryGetValue(objVarName, out var objVarType))
                            {
                                if (_methodReturnTypes.TryGetValue((objVarType, methodName), out var returnType))
                                {
                                    _varTypes[name] = returnType;
                                    _addUsedType(returnType);
                                }
                            }
                        }
                    }
                }
                else if (typeStr == "var" && variable.Initializer.Value is MemberAccessExpressionSyntax fieldAccess)
                {
                    var objExpr = fieldAccess.Expression;
                    var memberName = fieldAccess.Name.Identifier.Text;
                    
                    if (objExpr is IdentifierNameSyntax idSyntax)
                    {
                        var objVarName = idSyntax.Identifier.Text;
                        if (_varTypes.TryGetValue(objVarName, out var objVarType))
                        {
                            if (_fieldTypes.TryGetValue((objVarType, memberName), out var fieldType))
                            {
                                _varTypes[name] = fieldType;
                                _addUsedType(fieldType);
                            }
                        }
                    }
                }
                else if (typeStr != "var")
                {
                    _varTypes[name] = typeStr;
                }
            }
            else
            {
                lines.Add($"{type} {name};");
            }
        }

        return lines;
    }

    private string GenerateExpression(ExpressionSyntax expr)
    {
        switch (expr)
        {
            case LiteralExpressionSyntax literal:
                var token = literal.Token;
                if (token.IsKind(SyntaxKind.StringLiteralToken))
                    return $"\"{token.ValueText}\"";
                if (token.IsKind(SyntaxKind.TrueLiteralExpression) || token.IsKind(SyntaxKind.FalseLiteralExpression))
                    return token.Text;
                return token.ValueText;
            
            case IdentifierNameSyntax identifier:
                return identifier.Identifier.Text;
            
            case MemberAccessExpressionSyntax memberAccess:
                var obj = GenerateExpression(memberAccess.Expression);
                var member = memberAccess.Name.Identifier.Text;
                
                if (member.StartsWith("get_") || member.StartsWith("set_"))
                {
                    var prefix = member.Substring(0, 4);
                    var rest = member.Substring(4);
                    var lowerRest = char.ToLower(rest[0]) + rest.Substring(1);
                    return $"{obj}->{prefix}{lowerRest}()";
                }
                return $"{obj}->{member}";
            
            case InvocationExpressionSyntax invocation:
                var methodExpr = invocation.Expression;
                var args = string.Join(", ", invocation.ArgumentList.Arguments
                    .Select(a => GenerateExpression(a.Expression)));
                
                if (methodExpr is IdentifierNameSyntax identifierName)
                {
                    if (identifierName.Identifier.Text == _hook.Method.Name)
                    {
                        return $"{_hook.HookName}({args})";
                    }
                    return $"{identifierName.Identifier.Text}({args})";
                }
                
                if (methodExpr is MemberAccessExpressionSyntax memberAccessExpr)
                {
                    var methodName = memberAccessExpr.Name.Identifier.Text;
                    var objExpr = GenerateExpression(memberAccessExpr.Expression);
                    
                    if (methodName == _hook.Method.Name && objExpr == "this")
                    {
                        return $"{_hook.HookName}({args})";
                    }
                    
                    if (methodName == "WriteLine" || methodName == "Write")
                    {
                        return $"PaperLogger.info({args})";
                    }
                    
                    if (methodName.StartsWith("get_") || methodName.StartsWith("set_"))
                    {
                        var prefix = methodName.Substring(0, 4);
                        var rest = methodName.Substring(4);
                        var lowerRest = char.ToLower(rest[0]) + rest.Substring(1);
                        return $"{objExpr}->{prefix}{lowerRest}({args})";
                    }
                    
                    if (methodName == "GetComponentInChildren" || methodName == "GetComponent")
                    {
                        string typeArg = "";
                        if (memberAccessExpr.Name is GenericNameSyntax genericName && genericName.TypeArgumentList.Arguments.Count > 0)
                        {
                            typeArg = genericName.TypeArgumentList.Arguments[0].ToString();
                            _addUsedType(typeArg);
                        }
                        else if (invocation.ArgumentList.Arguments.Count > 0)
                        {
                            typeArg = GenerateExpression(invocation.ArgumentList.Arguments[0].Expression);
                        }
                        var ns = _getNamespaceForType(typeArg);
                        return $"{objExpr}->template GetComponentInChildren<{ns}::{typeArg}*>()";
                    }
                    
                    return $"{objExpr}->{methodName}({args})";
                }
                
                var method = GenerateExpression(methodExpr);
                return $"{method}({args})";
            
            case BinaryExpressionSyntax binary:
                var left = GenerateExpression(binary.Left);
                var right = GenerateExpression(binary.Right);
                return $"{left} {binary.OperatorToken.Text} {right}";
            
            case PrefixUnaryExpressionSyntax prefixUnary:
                var operand = GenerateExpression(prefixUnary.Operand);
                return $"{prefixUnary.OperatorToken.Text}{operand}";
            
            case PostfixUnaryExpressionSyntax postfixUnary:
                operand = GenerateExpression(postfixUnary.Operand);
                return $"{operand}{postfixUnary.OperatorToken.Text}";
            
            case AssignmentExpressionSyntax assignment:
                var assignLeft = GenerateExpression(assignment.Left);
                var assignRight = GenerateExpression(assignment.Right);
                return $"{assignLeft} {assignment.OperatorToken.Text} {assignRight}";
            
            case ParenthesizedExpressionSyntax paren:
                return $"({GenerateExpression(paren.Expression)})";
            
            case ObjectCreationExpressionSyntax objCreation:
                var objType = objCreation.Type.ToString();
                var objArgs = objCreation.ArgumentList != null
                    ? string.Join(", ", objCreation.ArgumentList.Arguments.Select(a => GenerateExpression(a.Expression)))
                    : "";
                return $"new {objType}({objArgs})";
            
            case ThisExpressionSyntax:
                return "this";
            
            case CastExpressionSyntax cast:
                var castType = _typeMapper.MapType(cast.Type.ToString());
                var castExpr = GenerateExpression(cast.Expression);
                return $"static_cast<{castType}>({castExpr})";
            
            case ConditionalExpressionSyntax conditional:
                var cond = GenerateExpression(conditional.Condition);
                var whenTrue = GenerateExpression(conditional.WhenTrue);
                var whenFalse = GenerateExpression(conditional.WhenFalse);
                return $"({cond} ? {whenTrue} : {whenFalse})";
            
            case GenericNameSyntax genericName:
                return genericName.Identifier.Text;
            
            default:
                return $"/* TODO: {expr.GetType().Name} */";
        }
    }

    private List<string> GenerateIfStatement(IfStatementSyntax ifStmt)
    {
        var lines = new List<string>();
        var condition = GenerateExpression(ifStmt.Condition);
        lines.Add($"if ({condition}) {{");
        
        if (ifStmt.Statement is BlockSyntax block)
        {
            foreach (var stmt in block.Statements)
            {
                lines.AddRange(GenerateStatement(stmt).Select(l => "    " + l));
            }
        }
        else
        {
            lines.AddRange(GenerateStatement(ifStmt.Statement).Select(l => "    " + l));
        }
        
        lines.Add("}");
        
        if (ifStmt.Else != null)
        {
            lines.Add("else {");
            if (ifStmt.Else.Statement is BlockSyntax elseBlock)
            {
                foreach (var stmt in elseBlock.Statements)
                {
                    lines.AddRange(GenerateStatement(stmt).Select(l => "    " + l));
                }
            }
            else if (ifStmt.Else.Statement is IfStatementSyntax elseIf)
            {
                lines.AddRange(GenerateIfStatement(elseIf).Select(l => "    " + l));
            }
            else
            {
                lines.AddRange(GenerateStatement(ifStmt.Else.Statement).Select(l => "    " + l));
            }
            lines.Add("}");
        }

        return lines;
    }

    private List<string> GenerateForEachStatement(ForEachStatementSyntax forEachStmt)
    {
        var lines = new List<string>();
        var type = _typeMapper.MapType(forEachStmt.Type.ToString());
        var identifier = forEachStmt.Identifier.Text;
        var expression = GenerateExpression(forEachStmt.Expression);
        
        lines.Add($"for (auto& {identifier} : {expression}) {{");
        
        if (forEachStmt.Statement is BlockSyntax block)
        {
            foreach (var stmt in block.Statements)
            {
                lines.AddRange(GenerateStatement(stmt).Select(l => "    " + l));
            }
        }
        
        lines.Add("}");
        return lines;
    }

    private List<string> GenerateWhileStatement(WhileStatementSyntax whileStmt)
    {
        var lines = new List<string>();
        var condition = GenerateExpression(whileStmt.Condition);
        
        lines.Add($"while ({condition}) {{");
        
        if (whileStmt.Statement is BlockSyntax block)
        {
            foreach (var stmt in block.Statements)
            {
                lines.AddRange(GenerateStatement(stmt).Select(l => "    " + l));
            }
        }
        
        lines.Add("}");
        return lines;
    }
}

class HookInfo
{
    public string HookName { get; set; } = "";
    public string TargetClass { get; set; } = "";
    public string TargetMethod { get; set; } = "";
    public MethodInfo Method { get; set; } = new();
}

class ClassInfo
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public bool IsStatic { get; set; }
    public List<MethodInfo> Methods { get; set; } = new();
    public List<FieldInfo> Fields { get; set; } = new();
    public List<PropertyInfo> Properties { get; set; } = new();
}

class MethodInfo
{
    public string Name { get; set; } = "";
    public string ReturnType { get; set; } = "void";
    public List<ParameterInfo> Parameters { get; set; } = new();
    public bool IsStatic { get; set; }
    public bool IsHook { get; set; }
    public BlockSyntax? Body { get; set; }
}

class FieldInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
}

class PropertyInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool HasGetter { get; set; }
    public bool HasSetter { get; set; }
}

class ParameterInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
}

class GeneratedFile
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
}

class ModInfo
{
    public string Id { get; set; } = "mod";
    public string Version { get; set; } = "1.0.0";
}

class ConfigValueInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public string DefaultValue { get; set; } = "";
}
