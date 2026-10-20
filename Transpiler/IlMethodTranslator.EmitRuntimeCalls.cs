using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed partial class IlMethodTranslator
{
    private bool TryBuildBsmlLiteIntrinsic(MethodReference method, IReadOnlyList<CppExpression> args, int indentLevel, out CppExpression expression)
    {
        expression = null!;
        if (method.HasThis || method.DeclaringType.FullName != "CoreMod.BSMLLite")
            return false;

        switch (method.Name)
        {
            case "Vector2" when args.Count == 2:
                RequiredIncludes.Add("UnityEngine/Vector2.hpp");
                expression = new CppExpression
                {
                    Code = $"UnityEngine::Vector2{{{args[0].Code}, {args[1].Code}}}",
                    Type = method.ReturnType,
                    PreferAutoDeclaration = true,
                };
                return true;
            case "Vector3" when args.Count == 3:
                RequiredIncludes.Add("UnityEngine/Vector3.hpp");
                expression = new CppExpression
                {
                    Code = $"UnityEngine::Vector3{{{args[0].Code}, {args[1].Code}, {args[2].Code}}}",
                    Type = method.ReturnType,
                    PreferAutoDeclaration = true,
                };
                return true;
            case "Color" when args.Count == 4:
                RequiredIncludes.Add("UnityEngine/Color.hpp");
                expression = new CppExpression
                {
                    Code = $"UnityEngine::Color{{{args[0].Code}, {args[1].Code}, {args[2].Code}, {args[3].Code}}}",
                    Type = method.ReturnType,
                    PreferAutoDeclaration = true,
                };
                return true;
        }

        var include = GetBsmlLiteInclude(method.Name);
        if (include == null)
            return false;

        RequiredIncludes.Add(include);
        RequiredInclude(method.ReturnType);

        var normalizedArgs = args.Select((arg, index) => NormalizeBsmlLiteArgument(method, arg, index)).ToList();
        var call = $"BSML::Lite::{method.Name}({string.Join(", ", normalizedArgs.Select(arg => arg.Code))})";
        expression = new CppExpression
        {
            Code = call,
            Type = method.ReturnType,
            PreferAutoDeclaration = true,
            HasSideEffects = true,
        };
        return true;
    }

    private static string? GetBsmlLiteInclude(string methodName)
    {
        return methodName switch
        {
            "AddHoverHint" or "CreateCanvas" or "CreateFloatingScreen" or "CreateProgressBar" => "bsml/shared/BSML-Lite/Creation/Misc.hpp",
            "CreateUIButton" or "SetButtonText" or "SetButtonTextSize" or "ToggleButtonWordWrapping" or "SetButtonIcon" or "SetButtonBackground" or "SetButtonSprites" => "bsml/shared/BSML-Lite/Creation/Buttons.hpp",
            "CreateText" or "CreateClickableText" => "bsml/shared/BSML-Lite/Creation/Text.hpp",
            "CreateImage" or "CreateClickableImage" or "CreateRawImage" or "FileToSprite" or "TextureToSprite" or "Base64ToSprite" or "ArrayToSprite" => "bsml/shared/BSML-Lite/Creation/Image.hpp",
            "CreateVerticalLayoutGroup" or "CreateHorizontalLayoutGroup" or "CreateGridLayoutGroup" or "CreateStackLayoutGroup" or "CreateScrollableSettingsContainer" or "CreateScrollView" or "CreateModal" or "CreateScrollableModalContainer" or "CreateModifierContainer" =>
                "bsml/shared/BSML-Lite/Creation/Layout.hpp",
            _ => null,
        };
    }

    private CppExpression NormalizeBsmlLiteArgument(MethodReference method, CppExpression arg, int index)
    {
        if (index < method.Parameters.Count && method.Parameters[index].ParameterType.FullName == "BSML.Side")
        {
            return CopyExpression(arg, $"static_cast<BSML::Side>({arg.Code})");
        }

        if (ShouldPassBsmlLiteStringView(method, index) && TryUnwrapNewStringLiteral(arg.Code, out var literal))
        {
            return CopyExpression(arg, literal);
        }

        return arg;
    }

    private static CppExpression CopyExpression(CppExpression source, string code)
    {
        return new CppExpression
        {
            Code = code,
            Type = source.Type,
            TypeToken = source.TypeToken,
            StringArrayElements = source.StringArrayElements,
            HasSideEffects = source.HasSideEffects,
            PreferAutoDeclaration = source.PreferAutoDeclaration,
        };
    }

    private static bool ShouldPassBsmlLiteStringView(MethodReference method, int index)
    {
        return method.Name switch
        {
            "CreateUIButton" => index == 2 && method.Parameters.Count > 2 && method.Parameters[index].ParameterType.FullName == "System.String",
            "FileToSprite" or "Base64ToSprite" => index == 0,
            _ => false,
        };
    }

    private bool ShouldUseRuntimeMethodInvocation(MethodReference method)
    {
        if (method.Name == ".ctor")
            return false;

        var declaringType = NormalizeTypeReference(method.DeclaringType);
        if (declaringType == null)
            return false;

        var namespaceName = declaringType.Namespace ?? "";
        if (!(namespaceName.Equals("System", StringComparison.Ordinal) || namespaceName.StartsWith("System.", StringComparison.Ordinal)))
            return false;

        return !method.HasThis || !IsValueType(declaringType);
    }

    private string GetRuntimeClassName(TypeReference type)
    {
        var names = new Stack<string>();
        TypeReference? current = type;
        while (current != null)
        {
            names.Push(current.Name.Split('`')[0]);
            current = current.DeclaringType;
        }

        return string.Join("/", names);
    }

    private TypeReference ResolveEffectiveReturnType(MethodReference method, CppExpression? instance)
    {
        if (method is GenericInstanceMethod genericMethod)
            return ResolveGenericReturnType(genericMethod);

        if (method.Name == "get_Item")
        {
            var listInstance = NormalizeTypeReference(instance?.Type) as GenericInstanceType;
            if (listInstance != null && listInstance.GenericArguments.Count > 0)
                return listInstance.GenericArguments[0];
        }

        if (method.ReturnType is not GenericParameter genericParameter)
            return method.ReturnType;

        var genericInstance = NormalizeTypeReference(method.DeclaringType) as GenericInstanceType ?? NormalizeTypeReference(instance?.Type) as GenericInstanceType;
        if (genericInstance != null && genericParameter.Position >= 0 && genericParameter.Position < genericInstance.GenericArguments.Count)
            return genericInstance.GenericArguments[genericParameter.Position];

        return method.ReturnType;
    }

    private static TypeReference ResolveGenericReturnType(GenericInstanceMethod method)
    {
        return SubstituteGenericReturnType(method.ReturnType, method);
    }

    private static TypeReference SubstituteGenericReturnType(TypeReference type, GenericInstanceMethod method)
    {
        if (type is GenericParameter genericParameter && genericParameter.Type == GenericParameterType.Method && genericParameter.Position >= 0 && genericParameter.Position < method.GenericArguments.Count)
            return method.GenericArguments[genericParameter.Position];

        if (type is ArrayType arrayType)
            return new ArrayType(SubstituteGenericReturnType(arrayType.ElementType, method), arrayType.Rank);

        if (type is ByReferenceType byReferenceType)
            return new ByReferenceType(SubstituteGenericReturnType(byReferenceType.ElementType, method));

        if (type is PointerType pointerType)
            return new PointerType(SubstituteGenericReturnType(pointerType.ElementType, method));

        return type;
    }
}
