using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal static class IlControlFlowLowerer
{
    public static IReadOnlyList<QuestLogStatement> LowerRange(IlTranslationContext context, int start, int end, bool includeLocalDeclarations = false, int? loopBreakTarget = null, int? loopContinueTarget = null)
    {
        var output = new List<QuestLogStatement>();
        if (includeLocalDeclarations)
        {
            foreach (var configField in context.ConfigFields.Values)
                output.Add(new QuestLogStatement("", "", "IL Quest config declaration", null, null, "", configField));
            foreach (var (name, declaration) in context.StaticFieldDeclarations)
                output.Add(new QuestLogStatement("", "", "IL static primitive storage", null, null, "", StaticFields: [new QuestStaticField(name, declaration)]));
            foreach (var variable in context.Method.Body.Variables)
                output.Add(Raw($"{CppLocalType(variable.VariableType, context.Method)} il_local_{variable.Index}{{}};", "IL local declaration"));
        }

        var stack = new Stack<QuestValue>();
        for (var index = start; index < end; index++)
        {
            var instruction = context.Instructions[index];
            var code = instruction.OpCode.Code;
            if (TryTranslateDoWhile(context, index, end, out var doWhileEnd, out var doWhileCode))
            {
                output.Add(Raw(doWhileCode, "IL do/while loop"));
                index = doWhileEnd;
                continue;
            }
            if (code is Code.Br or Code.Br_S && TryTranslateWhile(context, index, end, out var loopEnd, out var loop))
            {
                output.Add(Raw(loop, "IL while loop"));
                index = loopEnd;
                continue;
            }
            if (code is Code.Br or Code.Br_S or Code.Leave or Code.Leave_S)
            {
                if (instruction.Operand is Instruction nextTarget && context.InstructionIndexes.TryGetValue(nextTarget, out var nextIndex) && nextIndex == index + 1)
                    continue;
                if (instruction.Operand is Instruction branchTarget && context.InstructionIndexes.TryGetValue(branchTarget, out var branchIndex) && branchIndex == loopBreakTarget)
                {
                    output.Add(Raw("break;", "IL loop break"));
                    continue;
                }
                if (instruction.Operand is Instruction continueTarget && context.InstructionIndexes.TryGetValue(continueTarget, out var continueIndex) && continueIndex == loopContinueTarget)
                {
                    output.Add(Raw("continue;", "IL loop continue"));
                    continue;
                }
                throw Unsupported(context.Method, instruction, "branch does not match a structured loop break/continue or if/else shape");
            }
            if (code is Code.Brfalse or Code.Brfalse_S or Code.Brtrue or Code.Brtrue_S)
            {
                if (
                    stack.Count == 1
                    && instruction.Operand is Instruction returnTarget
                    && context.InstructionIndexes.TryGetValue(returnTarget, out var returnTargetIndex)
                    && returnTargetIndex < context.Instructions.Count
                    && context.Instructions[returnTargetIndex].OpCode.Code == Code.Ret
                    && context.Method.ReturnType.MetadataType == MetadataType.Void
                )
                {
                    var condition = ToBoolean(stack.Pop());
                    var returnOnTrue = code is Code.Brtrue or Code.Brtrue_S;
                    output.Add(Raw(FormatBlock($"if ({(returnOnTrue ? condition : $"!({condition})")})", [Raw("return;", "IL branch to return")]), "IL branch to return"));
                    continue;
                }
                if (
                    stack.Count == 1
                    && instruction.Operand is Instruction earlyReturnTarget
                    && context.InstructionIndexes.TryGetValue(earlyReturnTarget, out var earlyReturnTargetIndex)
                    && earlyReturnTargetIndex == end + 1
                    && end < context.Instructions.Count
                    && context.Instructions[end].OpCode.Code == Code.Ret
                    && context.Method.ReturnType.MetadataType == MetadataType.Void
                )
                {
                    var condition = ToBoolean(stack.Pop());
                    var returnOnTrue = code is Code.Brfalse or Code.Brfalse_S;
                    output.Add(Raw(FormatBlock($"if ({(returnOnTrue ? condition : $"!({condition})")})", [Raw("return;", "IL conditional early return")]), "IL conditional early return"));
                    index = earlyReturnTargetIndex - 1;
                    continue;
                }
                if (stack.Count == 1 && instruction.Operand is Instruction loopTarget && context.InstructionIndexes.TryGetValue(loopTarget, out var loopTargetIndex) && loopTargetIndex == loopBreakTarget)
                {
                    var condition = ToBoolean(stack.Pop());
                    var breakOnTrue = code is Code.Brtrue or Code.Brtrue_S;
                    output.Add(Raw(FormatBlock($"if ({(breakOnTrue ? condition : $"!({condition})")})", [Raw("break;", "IL conditional loop break")]), "IL conditional loop break"));
                    continue;
                }
                if (stack.Count != 1 || instruction.Operand is not Instruction target || !context.InstructionIndexes.TryGetValue(target, out var targetIndex) || targetIndex <= index || targetIndex > end)
                    throw Unsupported(context.Method, instruction, $"invalid forward conditional branch (targetIndex={(instruction.Operand is Instruction branch ? context.InstructionIndexes.GetValueOrDefault(branch, -1) : -1)}, instructionIndex={index}, rangeEnd={end}, stackDepth={stack.Count})");
                var truth = ToBoolean(stack.Pop());
                var branchOnTrue = code is Code.Brtrue or Code.Brtrue_S;
                if (TryTranslateIfElse(context, index, targetIndex, end, truth, branchOnTrue, loopBreakTarget, loopContinueTarget, out var ifElseEnd, out var ifElse, out var mergedValue))
                {
                    if (ifElse.Length > 0)
                        output.Add(Raw(ifElse, "IL if/else"));
                    if (mergedValue is not null)
                        stack.Push(mergedValue);
                    index = ifElseEnd;
                    continue;
                }
                var body = LowerRange(context, index + 1, targetIndex, loopBreakTarget: loopBreakTarget, loopContinueTarget: loopContinueTarget);
                output.Add(Raw(FormatBlock($"if ({(branchOnTrue ? $"!({truth})" : truth)})", body), "IL if"));
                index = targetIndex - 1;
                continue;
            }
            if (IlInstructionLowerer.IsRelationalBranch(code))
            {
                if (stack.Count == 2 && instruction.Operand is Instruction loopTarget && context.InstructionIndexes.TryGetValue(loopTarget, out var loopTargetIndex) && loopTargetIndex == loopBreakTarget)
                {
                    var breakRight = stack.Pop();
                    var breakLeft = stack.Pop();
                    var breakCondition = IlComparisonLowerer.Format(breakLeft, breakRight, code);
                    output.Add(Raw(FormatBlock($"if ({breakCondition})", [Raw("break;", "IL conditional loop break")]), "IL conditional loop break", breakLeft.ApiMapping ?? breakRight.ApiMapping));
                    continue;
                }
                if (stack.Count != 2 || instruction.Operand is not Instruction target || !context.InstructionIndexes.TryGetValue(target, out var targetIndex) || targetIndex <= index || targetIndex > end)
                    throw Unsupported(
                        context.Method,
                        instruction,
                        $"invalid forward comparison branch (target={instruction.Operand}, instructionIndex={index}, targetIndex={(instruction.Operand is Instruction branch ? context.InstructionIndexes.GetValueOrDefault(branch, -1) : -1)}, rangeEnd={end})"
                    );
                var right = stack.Pop();
                var left = stack.Pop();
                var condition = IlComparisonLowerer.Format(left, right, code);
                var body = LowerRange(context, index + 1, targetIndex, loopBreakTarget: loopBreakTarget, loopContinueTarget: loopContinueTarget);
                output.Add(Raw(FormatBlock($"if (!{condition})", body), "IL comparison if", left.ApiMapping ?? right.ApiMapping));
                index = targetIndex - 1;
                continue;
            }
            if (code == Code.Switch)
            {
                if (stack.Count != 1 || instruction.Operand is not Instruction[] targets)
                    throw Unsupported(context.Method, instruction, "invalid switch selector");
                var selector = stack.Pop();
                if (!TryTranslateSwitch(context, index, end, selector, targets, out var switchEnd, out var switchCode))
                    throw Unsupported(context.Method, instruction, "switch arms do not form non-fallthrough structured blocks");
                output.Add(Raw(switchCode, "IL switch", selector.ApiMapping));
                index = switchEnd;
                continue;
            }
            if (IlInstructionLowerer.TryLower(context, instruction, stack, out var statement) && statement is not null)
                output.Add(statement);
        }
        if (stack.Count != 0)
            throw new InvalidOperationException($"Evaluation stack is not empty at the end of IL range [{start}, {end}) in {context.Method.FullName}: {string.Join(", ", stack.Reverse().Select(value => $"{value.PcType} ({value.Code})"))}.");
        return output;
    }

    private static bool TryTranslateDoWhile(IlTranslationContext context, int start, int end, out int loopEnd, out string code)
    {
        loopEnd = start;
        code = "";
        if (end <= start + 1)
            return false;

        for (var branchIndex = start + 1; branchIndex < end; branchIndex++)
        {
            var branch = context.Instructions[branchIndex];
            if (!IlInstructionLowerer.IsConditionalBranch(branch.OpCode.Code) || branch.Operand is not Instruction bodyTarget || !context.InstructionIndexes.TryGetValue(bodyTarget, out var bodyStart) || bodyStart != start)
                continue;

            var conditionStart = -1;
            string? condition = null;
            IReadOnlyList<QuestLogStatement> conditionStatements = Array.Empty<QuestLogStatement>();
            for (var candidate = start; candidate < branchIndex; candidate++)
            {
                if (!IlInstructionLowerer.TryLowerCondition(context, candidate, branchIndex, branch, out var evaluated, out var loweredCondition))
                    continue;
                conditionStart = candidate;
                condition = evaluated;
                conditionStatements = loweredCondition;
                break;
            }
            if (conditionStart < 0 || condition is null)
                throw Unsupported(context.Method, branch, "do/while condition is not a verified pure suffix of local/argument loads, constants, and arithmetic; method calls and assignments cannot be moved into the condition");

            var repeatsWhenTrue = branch.OpCode.Code is not (Code.Brfalse or Code.Brfalse_S);
            var whileCondition = repeatsWhenTrue ? condition : $"!({condition})";
            var body = LowerRange(context, start, conditionStart, loopBreakTarget: branchIndex + 1, loopContinueTarget: conditionStart).Concat(conditionStatements).ToArray();
            code = "do " + FormatBlock("", body) + $" while ({whileCondition});";
            loopEnd = branchIndex;
            return true;
        }
        return false;
    }

    private static bool TryTranslateWhile(IlTranslationContext context, int preheaderIndex, int end, out int loopEnd, out string code)
    {
        loopEnd = preheaderIndex;
        code = "";
        var preheader = context.Instructions[preheaderIndex];
        if (preheader.Operand is not Instruction conditionStart || !context.InstructionIndexes.TryGetValue(conditionStart, out var conditionIndex) || conditionIndex <= preheaderIndex + 1)
            return false;
        var bodyStart = preheaderIndex + 1;
        for (var branchIndex = conditionIndex; branchIndex < end; branchIndex++)
        {
            var branch = context.Instructions[branchIndex];
            if (!IlInstructionLowerer.IsConditionalBranch(branch.OpCode.Code) || branch.Operand is not Instruction backTarget || !context.InstructionIndexes.TryGetValue(backTarget, out var targetIndex) || targetIndex != bodyStart)
                continue;
            var body = LowerRange(context, bodyStart, conditionIndex, loopBreakTarget: branchIndex + 1, loopContinueTarget: conditionIndex);
            if (IlInstructionLowerer.TryEvaluateCondition(context, conditionIndex, branchIndex, branch, out var condition))
            {
                code = FormatBlock($"while ({condition})", body);
            }
            else if (IlInstructionLowerer.TryLowerCondition(context, conditionIndex, branchIndex, branch, out condition, out var conditionStatements))
            {
                var guardedBody = conditionStatements.Append(Raw($"if (!({condition})) break;", "IL loop condition")).Concat(body).ToArray();
                code = FormatBlock("while (true)", guardedBody);
            }
            else
            {
                continue;
            }
            loopEnd = branchIndex;
            return true;
        }
        return false;
    }

    private static bool TryTranslateIfElse(IlTranslationContext context, int branchIndex, int elseStart, int end, string truth, bool branchOnTrue, int? loopBreakTarget, int? loopContinueTarget, out int elseEnd, out string code, out QuestValue? mergedValue)
    {
        elseEnd = elseStart;
        code = "";
        mergedValue = null;
        if (branchOnTrue || elseStart <= branchIndex + 1)
            return false;
        var jumpOverElse = context.Instructions[elseStart - 1];
        if (jumpOverElse.OpCode.Code is not (Code.Br or Code.Br_S) || jumpOverElse.Operand is not Instruction join || !context.InstructionIndexes.TryGetValue(join, out var joinIndex) || joinIndex <= elseStart || joinIndex > end || joinIndex == loopBreakTarget || joinIndex == loopContinueTarget)
            return false;
        if (
            IlInstructionLowerer.TryEvaluateValue(context, branchIndex + 1, elseStart - 1, out var thenValue)
            && IlInstructionLowerer.TryEvaluateValue(context, elseStart, joinIndex, out var elseValue)
            && (thenValue.PcType == elseValue.PcType || thenValue.PcType == "System.Boolean" && elseValue.PcType == "System.Int32" || thenValue.PcType == "System.Int32" && elseValue.PcType == "System.Boolean")
        )
        {
            var boolResult = thenValue.PcType == "System.Boolean" || elseValue.PcType == "System.Boolean";
            var thenCode = boolResult ? ToBoolean(thenValue) : thenValue.Code;
            var elseCode = boolResult ? ToBoolean(elseValue) : elseValue.Code;
            mergedValue = new QuestValue($"({truth} ? {thenCode} : {elseCode})", boolResult ? "System.Boolean" : thenValue.PcType, false, thenValue.ApiMapping ?? elseValue.ApiMapping);
            elseEnd = joinIndex - 1;
            return true;
        }
        var thenBody = LowerRange(context, branchIndex + 1, elseStart - 1, loopBreakTarget: loopBreakTarget, loopContinueTarget: loopContinueTarget);
        var elseBody = LowerRange(context, elseStart, joinIndex, loopBreakTarget: loopBreakTarget, loopContinueTarget: loopContinueTarget);
        code = FormatBlock($"if ({truth})", thenBody) + " else " + FormatBlock("", elseBody);
        elseEnd = joinIndex - 1;
        return true;
    }

    private static bool TryTranslateSwitch(IlTranslationContext context, int switchIndex, int end, QuestValue selector, Instruction[] targets, out int switchEnd, out string code)
    {
        switchEnd = switchIndex;
        code = "";
        if (targets.Length == 0 || targets.Distinct().Count() != targets.Length || targets.Any(target => !context.InstructionIndexes.ContainsKey(target)))
            return false;
        var ordered = targets.Select((target, caseIndex) => (target, caseIndex, index: context.InstructionIndexes[target])).OrderBy(item => item.index).ToArray();
        var first = ordered[0].index;
        if (first <= switchIndex + 1)
            return false;
        var defaultEnd = first;
        var exitIndex = -1;
        var defaultBranch = context.Instructions[defaultEnd - 1];
        if (defaultBranch.OpCode.Code is Code.Br or Code.Br_S && defaultBranch.Operand is Instruction defaultTarget && context.InstructionIndexes.TryGetValue(defaultTarget, out var defaultExit))
        {
            exitIndex = defaultExit;
            defaultEnd--;
        }
        if (exitIndex < 0)
            for (var i = end - 1; i >= ordered[^1].index; i--)
                if (context.Instructions[i].OpCode.Code is Code.Br or Code.Br_S && context.Instructions[i].Operand is Instruction exitTarget && context.InstructionIndexes.TryGetValue(exitTarget, out exitIndex))
                    break;
        if (exitIndex <= ordered[^1].index || exitIndex >= end)
            return false;

        var lines = new List<string> { $"switch ({selector.Code}) {{" };
        foreach (var item in ordered)
        {
            var next = ordered.FirstOrDefault(candidate => candidate.index > item.index);
            var armEnd = next.target is null ? exitIndex : next.index;
            if (armEnd > item.index && context.Instructions[armEnd - 1].OpCode.Code is Code.Br or Code.Br_S && context.Instructions[armEnd - 1].Operand is Instruction armExit && context.InstructionIndexes.TryGetValue(armExit, out var armExitIndex) && armExitIndex == exitIndex)
                armEnd--;
            var body = LowerRange(context, item.index, armEnd);
            lines.Add($"case {item.caseIndex}:");
            lines.Add(Indent(Render(body)));
            lines.Add("    break;");
        }
        var defaultBody = LowerRange(context, switchIndex + 1, defaultEnd);
        lines.Add("default:");
        lines.Add(Indent(Render(defaultBody)));
        lines.Add("    break;");
        lines.Add("}");
        code = string.Join("\n", lines);
        switchEnd = exitIndex - 1;
        return true;
    }

    private static string ToBoolean(QuestValue value) =>
        value.PcType == "System.Boolean" ? value.Code
        : value.PcType == "System.Object" ? $"({value.Code} != nullptr)"
        : $"({value.Code} != 0)";

    private static string FormatBlock(string header, IReadOnlyList<QuestLogStatement> statements) => header.Length == 0 ? "{\n" + Indent(Render(statements)) + "\n}" : $"{header} {{\n{Indent(Render(statements))}\n}}";

    private static string Render(IEnumerable<QuestLogStatement> statements) =>
        string.Join("\n", statements.Select(statement => statement.RawCpp ?? (statement.QuestExpression is null ? $"PaperLogger.{statement.QuestLevel}(\"{Escape(statement.Message)}\");" : $"PaperLogger.{statement.QuestLevel}(\"{{}}\", {statement.QuestExpression});")));

    internal static string RenderBsmlAction(IEnumerable<QuestLogStatement> statements) => Render(statements.Where(statement => statement.ConfigField is null));

    internal static string RenderInlineHelper(IEnumerable<QuestLogStatement> statements) => Render(statements.Where(statement => statement.ConfigField is null && statement.StaticFields is null));

    private static string Indent(string source) => string.Join("\n", source.Split('\n').Select(line => "    " + line));

    private static QuestLogStatement Raw(string code, string source, ApiCallMapping? mapping = null, IReadOnlyList<QuestStaticField>? staticFields = null) => new("", "", source, null, mapping, code, StaticFields: staticFields);

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");

    internal static string CppLocalType(TypeReference type, MethodDefinition method)
    {
        if (type.MetadataType == MetadataType.Void)
            return "void";
        if (type is ByReferenceType byReference)
        {
            if (IsDelegateType(byReference.ElementType))
                throw new InvalidOperationException($"By-reference delegate local '{type.FullName}' has no verified Quest callback ABI in {method.FullName}.");
            throw new InvalidOperationException($"By-reference local '{type.FullName}' requires alias-aware storage that is not available in {method.FullName}.");
        }
        if (type is PointerType pointer)
            return CppLocalType(pointer.ElementType, method) + "*";
        if (type is ArrayType array)
        {
            if (!array.IsVector)
                throw new InvalidOperationException($"Array local type '{type.FullName}' is not a one-dimensional zero-based array in {method.FullName}.");
            if (IlArrayLowerer.TryCppArrayType(array.ElementType.FullName, out var elementCppType))
                return $"std::vector<{elementCppType}>";
            throw new InvalidOperationException($"Array local type '{type.FullName}' has no Quest storage representation in {method.FullName}.");
        }

        var primitive = type.FullName switch
        {
            "System.Boolean" => "bool",
            "System.Byte" => "uint8_t",
            "System.SByte" => "int8_t",
            "System.Int16" => "int16_t",
            "System.UInt16" => "uint16_t",
            "System.Int32" => "int32_t",
            "System.UInt32" => "uint32_t",
            "System.Int64" => "int64_t",
            "System.UInt64" => "uint64_t",
            "System.Single" => "float",
            "System.Double" => "double",
            "System.Char" => "char16_t",
            "System.String" => "::StringW",
            "System.Object" => "Il2CppObject*",
            "System.IntPtr" or "System.UIntPtr" => "void*",
            _ => null,
        };
        if (primitive is not null)
            return primitive;
        if (IsDelegateType(type))
            throw new InvalidOperationException($"Delegate type '{type.FullName}' has no verified Quest callback ABI in {method.FullName}.");
        if (type is GenericParameter)
            throw new InvalidOperationException($"Open generic local type '{type.FullName}' has no Quest representation in {method.FullName}.");
        if (type.Namespace == "System" || type.Namespace.StartsWith("System.", StringComparison.Ordinal))
            throw new InvalidOperationException($"CLR type '{type.FullName}' has no verified Quest C++ representation in {method.FullName}.");

        if (type is GenericInstanceType generic)
        {
            var args = string.Join(", ", generic.GenericArguments.Select(argument => CppLocalType(argument, method)));
            return CppObjectType(generic.ElementType) + "<" + args + ">*";
        }
        return CppObjectType(type) + (IsKnownValueType(type) ? "" : "*");
    }

    private static string CppObjectType(TypeReference type)
    {
        var cppNamespace = string.IsNullOrEmpty(type.Namespace) ? "GlobalNamespace" : string.Join("::", type.Namespace.Split('.', StringSplitOptions.RemoveEmptyEntries));
        return "::" + cppNamespace + "::" + type.Name.Split('`')[0];
    }

    private static bool IsKnownValueType(TypeReference type) => type.IsValueType || type.FullName is "UnityEngine.Vector2" or "UnityEngine.Vector3" or "UnityEngine.Vector4" or "UnityEngine.Color" or "UnityEngine.Quaternion";

    private static bool IsDelegateType(TypeReference type) =>
        type.FullName == "System.Action"
        || type.FullName.StartsWith("System.Action`", StringComparison.Ordinal)
        || type.FullName == "System.Func"
        || type.FullName.StartsWith("System.Func`", StringComparison.Ordinal)
        || type.FullName == "System.Delegate"
        || type.FullName == "System.MulticastDelegate";

    private static InvalidOperationException Unsupported(MethodDefinition method, Instruction instruction, string reason) => new($"Unsupported IL in {method.FullName} at IL_{instruction.Offset:x4} ({instruction.OpCode.Code}): {reason}.");
}
