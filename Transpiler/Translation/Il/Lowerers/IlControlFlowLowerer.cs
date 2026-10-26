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
                output.Add(Raw($"{CppLocalType(context.ResolveType(variable.VariableType), context.Method)} il_local_{variable.Index}{{}};", "IL local declaration"));
        }

        var stack = new Stack<QuestValue>();
        for (var index = start; index < end; index++)
        {
            var instruction = context.Instructions[index];
            var code = instruction.OpCode.Code;
            if (IsFinallyHandlerInstruction(context, index))
            {
                var handler = context.Method.Body.ExceptionHandlers.First(item => item.HandlerType == ExceptionHandlerType.Finally && context.InstructionIndexes[item.HandlerStart] <= index && (item.HandlerEnd is null || context.InstructionIndexes[item.HandlerEnd] > index));
                if (handler.HandlerEnd is not null && context.InstructionIndexes.TryGetValue(handler.HandlerEnd, out var handlerEnd))
                    index = handlerEnd - 1;
                continue;
            }
            if (TryLowerCachedDelegate(context, index, end, stack, out var delegateEnd, out var delegateValue))
            {
                stack.Push(delegateValue);
                index = delegateEnd;
                continue;
            }
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
                if (code is Code.Leave or Code.Leave_S && TryLowerEnumeratorFinally(context, instruction, out var disposeStatement))
                {
                    output.Add(disposeStatement);
                    continue;
                }
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
                    && context.EffectiveReturnType.MetadataType == MetadataType.Void
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
                    && context.EffectiveReturnType.MetadataType == MetadataType.Void
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
                    throw Unsupported(
                        context.Method,
                        instruction,
                        $"invalid forward conditional branch (targetIndex={(instruction.Operand is Instruction branch ? context.InstructionIndexes.GetValueOrDefault(branch, -1) : -1)}, instructionIndex={index}, range=[{start},{end}), stackDepth={stack.Count})"
                    );
                var truth = ToBoolean(stack.Pop());
                var branchOnTrue = code is Code.Brtrue or Code.Brtrue_S;
                if (TryTranslateShortCircuitIf(context, index, end, instruction, truth, out var shortCircuitEnd, out var shortCircuitCode))
                {
                    output.Add(Raw(shortCircuitCode, "IL short-circuit boolean condition"));
                    index = shortCircuitEnd;
                    continue;
                }
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
                if (TryTranslateIfElse(context, index, targetIndex, end, condition, branchOnTrue: true, loopBreakTarget, loopContinueTarget, out var mergedIfEnd, out var mergedIf, out var mergedValue))
                {
                    if (mergedIf.Length > 0)
                        output.Add(Raw(mergedIf, "IL relational if/else", left.ApiMapping ?? right.ApiMapping));
                    if (mergedValue is not null)
                        stack.Push(mergedValue);
                    index = mergedIfEnd;
                    continue;
                }
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

    private static bool IsFinallyHandlerInstruction(IlTranslationContext context, int index) =>
        context.Method.Body.ExceptionHandlers.Any(handler =>
            handler.HandlerType == ExceptionHandlerType.Finally
            && handler.HandlerStart is not null
            && context.InstructionIndexes.TryGetValue(handler.HandlerStart, out var start)
            && start <= index
            && (handler.HandlerEnd is null || context.InstructionIndexes.TryGetValue(handler.HandlerEnd, out var end) && index < end)
        );

    private static bool TryTranslateShortCircuitIf(IlTranslationContext context, int start, int end, Instruction firstBranch, string firstCondition, out int expressionEnd, out string code)
    {
        expressionEnd = start;
        code = "";
        if (firstBranch.Operand is not Instruction firstTarget || !context.InstructionIndexes.TryGetValue(firstTarget, out var firstTargetIndex))
            return false;
        var candidates = new SortedSet<int>();
        for (var index = start; index < Math.Min(end, start + 80); index++)
        {
            var candidate = context.Instructions[index];
            if (IlInstructionLowerer.IsConditionalBranch(candidate.OpCode.Code) && candidate.Operand is Instruction target && context.InstructionIndexes.TryGetValue(target, out var targetIndex) && targetIndex > start)
                candidates.Add(targetIndex);
        }
        foreach (var bodyStart in candidates.Where(candidate => candidate > firstTargetIndex))
        foreach (var joinIndex in candidates.Where(candidate => candidate > bodyStart))
        {
            if (!TryBuildDecision(context, start, bodyStart, joinIndex, firstBranch, firstCondition, new HashSet<int>(), out var condition))
                continue;
            IReadOnlyList<QuestLogStatement> body;
            try
            {
                body = LowerRange(context, bodyStart, joinIndex);
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            code = FormatBlock($"if ({condition})", body);
            expressionEnd = joinIndex - 1;
            return true;
        }
        return false;
    }

    private static bool TryBuildDecision(IlTranslationContext context, int start, int bodyStart, int joinIndex, Instruction? firstBranch, string firstCondition, HashSet<int> active, out string reachesBody)
    {
        reachesBody = "";
        if (start == bodyStart)
        {
            reachesBody = "true";
            return true;
        }
        if (start == joinIndex)
        {
            reachesBody = "false";
            return true;
        }
        if (start < 0 || start >= joinIndex || !active.Add(start))
            return false;
        try
        {
            var branchIndex = start;
            string condition;
            Instruction branch;
            IReadOnlyList<QuestLogStatement> conditionStatements;
            if (firstBranch is not null)
            {
                branch = firstBranch;
                condition = firstCondition;
                conditionStatements = Array.Empty<QuestLogStatement>();
            }
            else
            {
                while (branchIndex < joinIndex && !IlInstructionLowerer.IsConditionalBranch(context.Instructions[branchIndex].OpCode.Code))
                    branchIndex++;
                if (branchIndex >= joinIndex)
                    return false;
                branch = context.Instructions[branchIndex];
                if (!IlInstructionLowerer.TryLowerCondition(context, start, branchIndex, branch, out condition, out conditionStatements) || conditionStatements.Count != 0)
                    return false;
            }
            if (branch.Operand is not Instruction target || !context.InstructionIndexes.TryGetValue(target, out var targetIndex) || targetIndex <= branchIndex || targetIndex > joinIndex)
                return false;
            var fallthroughIndex = branchIndex + 1;
            var targetIsBody = TryBuildDecision(context, targetIndex, bodyStart, joinIndex, null, "", active, out var targetResult);
            var fallthroughIsBody = TryBuildDecision(context, fallthroughIndex, bodyStart, joinIndex, null, "", active, out var fallthroughResult);
            if (!targetIsBody || !fallthroughIsBody)
                return false;
            var branchWhen =
                IlInstructionLowerer.IsRelationalBranch(branch.OpCode.Code) ? condition
                : branch.OpCode.Code is Code.Brfalse or Code.Brfalse_S ? $"!({condition})"
                : condition;
            reachesBody = $"(({branchWhen}) ? ({targetResult}) : ({fallthroughResult}))";
            return true;
        }
        finally
        {
            active.Remove(start);
        }
    }

    private static bool TryLowerEnumeratorFinally(IlTranslationContext context, Instruction leave, out QuestLogStatement statement)
    {
        statement = null!;
        if (leave.Operand is not Instruction target || !context.InstructionIndexes.TryGetValue(target, out var targetIndex))
            return false;
        var handler = context.Method.Body.ExceptionHandlers.FirstOrDefault(item => item.HandlerType == ExceptionHandlerType.Finally && item.HandlerEnd is not null && context.InstructionIndexes.TryGetValue(item.HandlerEnd, out var end) && end == targetIndex);
        if (handler?.HandlerStart is null || !context.InstructionIndexes.TryGetValue(handler.HandlerStart, out var start))
            return false;
        var endIndex = handler.HandlerEnd is not null && context.InstructionIndexes.TryGetValue(handler.HandlerEnd, out var handlerEnd) ? handlerEnd : context.Instructions.Count;
        if (
            start + 3 >= endIndex
            || !TryGetLocal(context, context.Instructions[start], out var firstLocal)
            || context.Instructions[start + 1].OpCode.Code is not (Code.Brfalse or Code.Brfalse_S)
            || context.Instructions[start + 2].OpCode.Code is not (Code.Ldloc or Code.Ldloc_S or Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3)
            || !TryGetLocal(context, context.Instructions[start + 2], out var secondLocal)
            || firstLocal.Index != secondLocal.Index
            || context.Instructions[start + 3].OpCode.Code != Code.Callvirt
            || context.Instructions[start + 3].Operand is not MethodReference dispose
            || dispose.Name != "Dispose"
            || endIndex != start + 5
            || context.Instructions[start + 4].OpCode.Code != Code.Endfinally
        )
            return false;
        var mapping = context.Catalog.ResolveApiCall(dispose);
        if (mapping is null)
            return false;
        var enumeratorType = context.ResolveType(firstLocal.VariableType);
        var receiver = $"il_local_{firstLocal.Index}";
        if (IlInterfaceConversion.TryConvert(enumeratorType, dispose.DeclaringType, receiver, out var convertedReceiver, dispose.Name))
            receiver = convertedReceiver;
        statement = Raw($"if (il_local_{firstLocal.Index} != nullptr) {receiver}->Dispose();", "IL enumerator finally dispose", mapping);
        return true;
    }

    private static bool TryGetLocal(IlTranslationContext context, Instruction instruction, out VariableDefinition local)
    {
        if (instruction.Operand is VariableDefinition variable)
        {
            local = variable;
            return true;
        }
        var index = instruction.OpCode.Code switch
        {
            Code.Ldloc_0 => 0,
            Code.Ldloc_1 => 1,
            Code.Ldloc_2 => 2,
            Code.Ldloc_3 => 3,
            _ => -1,
        };
        if (index >= 0 && index < context.Method.Body.Variables.Count)
        {
            local = context.Method.Body.Variables[index];
            return true;
        }
        local = null!;
        return false;
    }

    private static bool TryLowerCachedDelegate(IlTranslationContext context, int start, int end, Stack<QuestValue> stack, out int sequenceEnd, out QuestValue delegateValue)
    {
        sequenceEnd = start;
        delegateValue = default!;
        if (start + 8 >= end || context.Instructions[start].OpCode.Code != Code.Ldsfld || context.Instructions[start].Operand is not FieldReference cacheReference)
            return false;
        var cache = cacheReference.Resolve();
        if (cache is null || !IlTranslationContext.IsCompilerDelegateCache(cache) || !cache.Name.StartsWith("<>9__", StringComparison.Ordinal))
            return false;
        if (
            context.Instructions[start + 1].OpCode.Code != Code.Dup
            || context.Instructions[start + 2].OpCode.Code is not (Code.Brtrue or Code.Brtrue_S)
            || context.Instructions[start + 3].OpCode.Code != Code.Pop
            || context.Instructions[start + 4].OpCode.Code != Code.Ldsfld
            || context.Instructions[start + 4].Operand is not FieldReference singletonReference
            || context.Instructions[start + 5].OpCode.Code != Code.Ldftn
            || context.Instructions[start + 5].Operand is not MethodReference targetReference
            || context.Instructions[start + 6].OpCode.Code != Code.Newobj
            || context.Instructions[start + 6].Operand is not MethodReference delegateConstructor
            || context.Instructions[start + 7].OpCode.Code != Code.Dup
            || context.Instructions[start + 8].OpCode.Code != Code.Stsfld
            || context.Instructions[start + 8].Operand is not FieldReference storeReference
        )
            return false;
        if (
            storeReference.Resolve()?.FullName != cache.FullName
            || singletonReference.Resolve() is not { } singleton
            || !IlTranslationContext.IsCompilerDelegateCache(singleton)
            || context.Instructions[start + 2].Operand is not Instruction branchTarget
            || !context.InstructionIndexes.TryGetValue(branchTarget, out var targetIndex)
            || targetIndex != start + 9
        )
            return false;
        var target = targetReference.Resolve();
        var delegateDefinition = delegateConstructor.Resolve();
        if (target is null || delegateDefinition is null || delegateConstructor.Parameters.Count != 2 || delegateConstructor.DeclaringType.Resolve()?.BaseType?.FullName != "System.MulticastDelegate")
            return false;
        var closureType = QuestApiTypeMapper.Map(singleton.FieldType);
        var nativeDelegate = QuestApiTypeMapper.Map(delegateConstructor.DeclaringType);
        if (closureType is null || nativeDelegate is null)
            return false;
        var delegateNativeName = nativeDelegate.TrimEnd('*');
        var closureConstruction = $"{closureType.TrimEnd('*')}::New_ctor()";
        var methodPointer = $"::System::IntPtr(reinterpret_cast<void*>(&{GeneratedModTypeEmitter.FunctionThunkName(target)}))";
        var expression = $"{delegateNativeName}::New_ctor({closureConstruction}, {methodPointer})";
        var nativeHeader = delegateNativeName.Split('<')[0].TrimStart(':').Replace("::", "/", StringComparison.Ordinal) + ".hpp";
        var nativeDef = Path.Combine(Path.GetDirectoryName(nativeHeader) ?? "", "zzzz__" + delegateNativeName.Split('<')[0].Split("::").Last() + "_def.hpp").Replace('\\', '/');
        var mapping = new ApiCallMapping(
            context.Catalog.Profile.Id,
            delegateDefinition.Module.Assembly.Name.Name,
            delegateDefinition.DeclaringType.FullName,
            delegateDefinition.Name,
            delegateDefinition.GenericParameters.Count,
            delegateDefinition.ReturnType.FullName,
            delegateDefinition.Parameters.Select(parameter => parameter.ParameterType.FullName).ToArray(),
            false,
            delegateNativeName.TrimStart(':'),
            nativeHeader,
            nativeDef,
            "New_ctor",
            nativeDelegate,
            ["::System::Object*", "::System::IntPtr"],
            true
        );
        delegateValue = new QuestValue(expression, delegateConstructor.DeclaringType.FullName, false, mapping);
        sequenceEnd = start + 8;
        return true;
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
        if (elseStart <= branchIndex + 1)
            return false;

        if (branchOnTrue)
        {
            for (var jumpIndex = elseStart - 1; jumpIndex > branchIndex; jumpIndex--)
            {
                var jump = context.Instructions[jumpIndex];
                if (
                    jump.OpCode.Code is not (Code.Br or Code.Br_S)
                    || jump.Operand is not Instruction joinOnFalse
                    || !context.InstructionIndexes.TryGetValue(joinOnFalse, out var joinOnFalseIndex)
                    || joinOnFalseIndex <= elseStart
                    || joinOnFalseIndex > end
                    || joinOnFalseIndex == loopBreakTarget
                    || joinOnFalseIndex == loopContinueTarget
                )
                    continue;

                if (
                    IlInstructionLowerer.TryEvaluateValue(context, branchIndex + 1, jumpIndex, out var falseValue)
                    && IlInstructionLowerer.TryEvaluateValue(context, elseStart, joinOnFalseIndex, out var trueValue)
                    && (trueValue.PcType == falseValue.PcType || trueValue.PcType == "System.Boolean" && falseValue.PcType == "System.Int32" || trueValue.PcType == "System.Int32" && falseValue.PcType == "System.Boolean")
                )
                {
                    var boolResult = trueValue.PcType == "System.Boolean" || falseValue.PcType == "System.Boolean";
                    var trueCode = boolResult ? ToBoolean(trueValue) : trueValue.Code;
                    var falseCode = boolResult ? ToBoolean(falseValue) : falseValue.Code;
                    mergedValue = new QuestValue($"({truth} ? {trueCode} : {falseCode})", boolResult ? "System.Boolean" : trueValue.PcType, false, trueValue.ApiMapping ?? falseValue.ApiMapping);
                    elseEnd = joinOnFalseIndex - 1;
                    return true;
                }

                var falseBody = LowerRange(context, branchIndex + 1, jumpIndex, loopBreakTarget: loopBreakTarget, loopContinueTarget: loopContinueTarget);
                var trueBody = LowerRange(context, elseStart, joinOnFalseIndex, loopBreakTarget: loopBreakTarget, loopContinueTarget: loopContinueTarget);
                code = FormatBlock($"if ({truth})", trueBody) + " else " + FormatBlock("", falseBody);
                elseEnd = joinOnFalseIndex - 1;
                return true;
            }
            return false;
        }

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
        if (exitIndex < 0 && IsReturnTerminatedSwitch(context, switchIndex, end, ordered, defaultEnd))
        {
            var terminalLines = new List<string> { $"switch ({selector.Code}) {{" };
            foreach (var item in ordered)
            {
                var next = ordered.FirstOrDefault(candidate => candidate.index > item.index);
                var armEnd = next.target is null ? end : next.index;
                var body = LowerRange(context, item.index, armEnd);
                terminalLines.Add($"case {item.caseIndex}:");
                terminalLines.Add(Indent(Render(body)));
            }
            var defaultTerminalBody = LowerRange(context, switchIndex + 1, defaultEnd);
            terminalLines.Add("default:");
            terminalLines.Add(Indent(Render(defaultTerminalBody)));
            terminalLines.Add("}");
            code = string.Join("\n", terminalLines);
            switchEnd = end - 1;
            return true;
        }
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

    private static bool IsReturnTerminatedSwitch(IlTranslationContext context, int switchIndex, int end, (Instruction target, int caseIndex, int index)[] ordered, int defaultEnd)
    {
        bool Returns(int start, int finish) => finish > start && context.Instructions[finish - 1].OpCode.Code == Code.Ret;
        if (!Returns(switchIndex + 1, defaultEnd))
            return false;
        for (var index = 0; index < ordered.Length; index++)
        {
            var finish = index + 1 < ordered.Length ? ordered[index + 1].index : end;
            if (!Returns(ordered[index].index, finish))
                return false;
        }
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
        if (type.Resolve()?.IsEnum == true)
            return "int32_t";
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
            return QuestApiTypeMapper.Map(array) ?? throw new InvalidOperationException($"Array local type '{type.FullName}' has no Quest storage representation in {method.FullName}.");
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
        if (QuestApiTypeMapper.Map(type) is { } mappedType)
            return mappedType;
        if (type is GenericParameter)
            throw new InvalidOperationException($"Open generic local type '{type.FullName}' has no Quest representation in {method.FullName}.");
        if (type is GenericInstanceType generic)
        {
            return QuestApiTypeMapper.Map(generic) ?? throw new InvalidOperationException($"Generic local type '{type.FullName}' has no Quest representation in {method.FullName}.");
        }
        if (type.Namespace == "System" || type.Namespace.StartsWith("System.", StringComparison.Ordinal))
            throw new InvalidOperationException($"CLR type '{type.FullName}' has no verified Quest C++ representation in {method.FullName}.");
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
