using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed partial class IlMethodTranslator
{
    private void TranslateRange(int startIndex, int endIndex, int indentLevel)
    {
        for (var index = startIndex; index < endIndex; index++)
        {
            var instruction = _instructions[index];
            var consumedUntil = index;

            if (TryEmitStructuredLoop(instruction, index, endIndex, indentLevel, out consumedUntil))
            {
                index = consumedUntil - 1;
                continue;
            }

            if (TryEmitStructuredIfFromLocalTemp(index, endIndex, indentLevel, out consumedUntil))
            {
                index = consumedUntil - 1;
                continue;
            }

            if (TryEmitStructuredIf(instruction, index, endIndex, indentLevel, out consumedUntil))
            {
                index = consumedUntil - 1;
                continue;
            }

            EmitInstruction(instruction, indentLevel);

            if (instruction.OpCode.Code is Code.Br or Code.Br_S or Code.Leave or Code.Leave_S && TryBuildReturnFromBranchTarget((Instruction)instruction.Operand, out _))
                break;
        }
    }

    private bool TryEmitStructuredIfFromLocalTemp(int index, int endIndex, int indentLevel, out int consumedUntil)
    {
        consumedUntil = index;
        if (index + 2 >= endIndex)
            return false;

        if (!TryGetLocalIndex(_instructions[index], out var storedLocalIndex))
            return false;

        var next = _instructions[index + 1];
        if (!TryGetLoadedLocalIndex(next, out var loadedLocalIndex) || loadedLocalIndex != storedLocalIndex)
            return false;

        var branch = _instructions[index + 2];
        if (branch.OpCode.Code is not (Code.Brfalse or Code.Brfalse_S or Code.Brtrue or Code.Brtrue_S))
            return false;

        if (!TryGetBranchTargetIndex(branch, index + 2, endIndex, out var targetIndex))
            return false;

        var bodyStartIndex = index + 3;
        var positiveCondition = branch.OpCode.Code is Code.Brfalse or Code.Brfalse_S ? MakeExplicitBooleanCheck(Pop().Code, invert: false) : MakeExplicitBooleanCheck(Pop().Code, invert: true);
        positiveCondition = ExtendConditionChain(positiveCondition, ref bodyStartIndex, targetIndex, endIndex);
        return TryEmitStructuredConditionalBlock(positiveCondition, bodyStartIndex, targetIndex, endIndex, indentLevel, out consumedUntil);
    }

    private bool TryEmitStructuredLoop(Instruction instruction, int index, int endIndex, int indentLevel, out int consumedUntil)
    {
        consumedUntil = index;
        if (instruction.OpCode.Code is not (Code.Br or Code.Br_S))
            return false;

        if (!TryGetBranchTargetIndex(instruction, index, endIndex, out var conditionStartIndex))
            return false;

        var bodyStartIndex = index + 1;
        if (bodyStartIndex >= conditionStartIndex)
            return false;

        if (!TryBuildLoopCondition(conditionStartIndex, bodyStartIndex, endIndex, out var conditionExpression, out var loopBranchIndex))
            return false;

        var incrementStartIndex = FindLoopIncrementStart(bodyStartIndex, conditionStartIndex);
        var continueTargetIndex = incrementStartIndex < conditionStartIndex ? incrementStartIndex : conditionStartIndex;
        var continueLabel = $"loop_continue_{_loopLabelCounter++}";

        AppendLine(indentLevel, "while (true) {");
        AppendLine(indentLevel + 1, $"if (!({conditionExpression})) {{");
        AppendLine(indentLevel + 2, "break;");
        AppendLine(indentLevel + 1, "}");

        _continueLabelScopes.Push(new Dictionary<int, string> { [continueTargetIndex] = continueLabel, [conditionStartIndex] = continueLabel });
        _breakTargetScopes.Push(new HashSet<int> { loopBranchIndex + 1 });
        try
        {
            TranslateRange(bodyStartIndex, incrementStartIndex, indentLevel + 1);
            AppendLine(indentLevel + 1, $"{continueLabel}:;");
            TranslateRange(incrementStartIndex, conditionStartIndex, indentLevel + 1);
        }
        finally
        {
            _breakTargetScopes.Pop();
            _continueLabelScopes.Pop();
        }

        AppendLine(indentLevel, "}");
        consumedUntil = loopBranchIndex + 1;
        return true;
    }

    private bool TryEmitStructuredIf(Instruction instruction, int index, int endIndex, int indentLevel, out int consumedUntil)
    {
        consumedUntil = index;
        switch (instruction.OpCode.Code)
        {
            case Code.Brfalse:
            case Code.Brfalse_S:
            case Code.Brtrue:
            case Code.Brtrue_S:
            {
                if (!TryGetBranchTargetIndex(instruction, index, endIndex, out var targetIndex))
                    return false;

                var bodyStartIndex = index + 1;
                var positiveCondition = instruction.OpCode.Code is Code.Brfalse or Code.Brfalse_S ? MakeExplicitBooleanCheck(Pop().Code, invert: false) : MakeExplicitBooleanCheck(Pop().Code, invert: true);
                positiveCondition = ExtendConditionChain(positiveCondition, ref bodyStartIndex, targetIndex, endIndex);
                return TryEmitStructuredConditionalBlock(positiveCondition, bodyStartIndex, targetIndex, endIndex, indentLevel, out consumedUntil);
            }
            case Code.Beq:
            case Code.Beq_S:
            case Code.Bne_Un:
            case Code.Bne_Un_S:
            case Code.Bge:
            case Code.Bge_S:
            case Code.Bge_Un:
            case Code.Bge_Un_S:
            case Code.Bgt:
            case Code.Bgt_S:
            case Code.Bgt_Un:
            case Code.Bgt_Un_S:
            case Code.Ble:
            case Code.Ble_S:
            case Code.Ble_Un:
            case Code.Ble_Un_S:
            case Code.Blt:
            case Code.Blt_S:
            case Code.Blt_Un:
            case Code.Blt_Un_S:
            {
                if (!TryGetBranchTargetIndex(instruction, index, endIndex, out var targetIndex))
                    return false;

                var right = Pop();
                var left = Pop();
                var bodyStartIndex = index + 1;
                var positiveCondition = BuildBodyConditionForCompareBranch(instruction.OpCode.Code, left, right);
                positiveCondition = ExtendConditionChain(positiveCondition, ref bodyStartIndex, targetIndex, endIndex);
                return TryEmitStructuredConditionalBlock(positiveCondition, bodyStartIndex, targetIndex, endIndex, indentLevel, out consumedUntil);
            }
            default:
                return false;
        }
    }

    private bool TryEmitStructuredConditionalBlock(string positiveCondition, int bodyStartIndex, int bodyEndIndex, int endIndex, int indentLevel, out int consumedUntil)
    {
        if (TryBuildEarlyExit(bodyStartIndex, bodyEndIndex, out var exitStatement))
        {
            AppendLine(indentLevel, $"if ({positiveCondition}) {{");
            AppendLine(indentLevel + 1, exitStatement);
            AppendLine(indentLevel, "}");
            consumedUntil = bodyEndIndex;
            return true;
        }

        if (TryGetElseBranch(bodyStartIndex, bodyEndIndex, endIndex, out var thenEndIndex, out var elseEndIndex))
        {
            if (IsEffectivelyEmptyRange(bodyStartIndex, thenEndIndex))
            {
                AppendLine(indentLevel, $"if ({NegateCondition(positiveCondition)}) {{");
                TranslateRange(bodyEndIndex, elseEndIndex, indentLevel + 1);
                AppendLine(indentLevel, "}");
                consumedUntil = elseEndIndex;
                return true;
            }

            AppendLine(indentLevel, $"if ({positiveCondition}) {{");
            TranslateRange(bodyStartIndex, thenEndIndex, indentLevel + 1);
            AppendLine(indentLevel, "}");
            AppendLine(indentLevel, "else {");
            TranslateRange(bodyEndIndex, elseEndIndex, indentLevel + 1);
            AppendLine(indentLevel, "}");
            consumedUntil = elseEndIndex;
            return true;
        }

        AppendLine(indentLevel, $"if ({positiveCondition}) {{");
        TranslateRange(bodyStartIndex, bodyEndIndex, indentLevel + 1);
        AppendLine(indentLevel, "}");
        consumedUntil = bodyEndIndex;
        return true;
    }

    private string BuildBodyConditionForCompareBranch(Code opcode, CppExpression left, CppExpression right)
    {
        if (TryBuildSimplifiedCompareCondition(opcode, left, right, branchWhenTrue: false, out var simplified))
            return simplified;

        return opcode switch
        {
            Code.Beq or Code.Beq_S => $"({left.Code} != {right.Code})",
            Code.Bne_Un or Code.Bne_Un_S => $"({left.Code} == {right.Code})",
            Code.Bge or Code.Bge_S or Code.Bge_Un or Code.Bge_Un_S => $"({left.Code} < {right.Code})",
            Code.Bgt or Code.Bgt_S or Code.Bgt_Un or Code.Bgt_Un_S => $"({left.Code} <= {right.Code})",
            Code.Ble or Code.Ble_S or Code.Ble_Un or Code.Ble_Un_S => $"({left.Code} > {right.Code})",
            Code.Blt or Code.Blt_S or Code.Blt_Un or Code.Blt_Un_S => $"({left.Code} >= {right.Code})",
            _ => throw new NotSupportedException($"Unsupported compare branch opcode {opcode}"),
        };
    }

    private string BuildLoopConditionForCompareBranch(Code opcode, CppExpression left, CppExpression right)
    {
        if (TryBuildSimplifiedCompareCondition(opcode, left, right, branchWhenTrue: true, out var simplified))
            return simplified;

        return opcode switch
        {
            Code.Beq or Code.Beq_S => $"({left.Code} == {right.Code})",
            Code.Bne_Un or Code.Bne_Un_S => $"({left.Code} != {right.Code})",
            Code.Bge or Code.Bge_S or Code.Bge_Un or Code.Bge_Un_S => $"({left.Code} >= {right.Code})",
            Code.Bgt or Code.Bgt_S or Code.Bgt_Un or Code.Bgt_Un_S => $"({left.Code} > {right.Code})",
            Code.Ble or Code.Ble_S or Code.Ble_Un or Code.Ble_Un_S => $"({left.Code} <= {right.Code})",
            Code.Blt or Code.Blt_S or Code.Blt_Un or Code.Blt_Un_S => $"({left.Code} < {right.Code})",
            _ => throw new NotSupportedException($"Unsupported loop compare branch opcode {opcode}"),
        };
    }

    private string ExtendConditionChain(string initialCondition, ref int bodyStartIndex, int targetIndex, int endIndex)
    {
        var conditions = new List<string> { initialCondition };

        while (TryReadAdditionalCondition(ref bodyStartIndex, targetIndex, endIndex, out var additionalCondition))
            conditions.Add(additionalCondition);

        return conditions.Count == 1 ? initialCondition : string.Join(" && ", conditions.Select(condition => $"({condition})"));
    }

    private bool TryReadAdditionalCondition(ref int scanStartIndex, int targetIndex, int endIndex, out string positiveCondition)
    {
        positiveCondition = "";
        var snapshot = CaptureSnapshot();

        for (var index = scanStartIndex; index < targetIndex; index++)
        {
            var instruction = _instructions[index];
            if (TryReadBranchPositiveCondition(instruction, index, targetIndex, endIndex, out positiveCondition))
            {
                scanStartIndex = index + 1;
                return true;
            }

            var lineCount = Statements.Count;
            try
            {
                EmitInstruction(instruction, 0);
            }
            catch
            {
                RestoreSnapshot(snapshot);
                positiveCondition = "";
                return false;
            }

            if (Statements.Count != lineCount)
            {
                RestoreSnapshot(snapshot);
                positiveCondition = "";
                return false;
            }
        }

        RestoreSnapshot(snapshot);
        positiveCondition = "";
        return false;
    }

    private bool TryReadBranchPositiveCondition(Instruction instruction, int sourceIndex, int expectedTargetIndex, int endIndex, out string positiveCondition)
    {
        positiveCondition = "";

        switch (instruction.OpCode.Code)
        {
            case Code.Brfalse:
            case Code.Brfalse_S:
            case Code.Brtrue:
            case Code.Brtrue_S:
            {
                if (!TryGetBranchTargetIndex(instruction, sourceIndex, endIndex, out var targetIndex) || targetIndex != expectedTargetIndex)
                    return false;

                positiveCondition = instruction.OpCode.Code is Code.Brfalse or Code.Brfalse_S ? MakeExplicitBooleanCheck(Pop().Code, invert: false) : MakeExplicitBooleanCheck(Pop().Code, invert: true);
                return true;
            }
            case Code.Beq:
            case Code.Beq_S:
            case Code.Bne_Un:
            case Code.Bne_Un_S:
            case Code.Bge:
            case Code.Bge_S:
            case Code.Bge_Un:
            case Code.Bge_Un_S:
            case Code.Bgt:
            case Code.Bgt_S:
            case Code.Bgt_Un:
            case Code.Bgt_Un_S:
            case Code.Ble:
            case Code.Ble_S:
            case Code.Ble_Un:
            case Code.Ble_Un_S:
            case Code.Blt:
            case Code.Blt_S:
            case Code.Blt_Un:
            case Code.Blt_Un_S:
            {
                if (!TryGetBranchTargetIndex(instruction, sourceIndex, endIndex, out var targetIndex) || targetIndex != expectedTargetIndex)
                    return false;

                var right = Pop();
                var left = Pop();
                positiveCondition = BuildBodyConditionForCompareBranch(instruction.OpCode.Code, left, right);
                return true;
            }
            default:
                return false;
        }
    }

    private bool TryBuildLoopCondition(int conditionStartIndex, int bodyStartIndex, int endIndex, out string conditionExpression, out int loopBranchIndex)
    {
        conditionExpression = "";
        loopBranchIndex = -1;
        var snapshot = CaptureSnapshot();
        var initialLineCount = Statements.Count;

        try
        {
            for (var index = conditionStartIndex; index < endIndex; index++)
            {
                var instruction = _instructions[index];
                if (TryReadLoopBackConditionFromLocalTemp(index, bodyStartIndex, endIndex, out conditionExpression))
                {
                    if (Statements.Count != initialLineCount)
                        return false;

                    loopBranchIndex = index + 2;
                    return true;
                }

                if (IsLoopBackBranch(instruction, bodyStartIndex, endIndex, out conditionExpression))
                {
                    if (Statements.Count != initialLineCount)
                        return false;

                    loopBranchIndex = index;
                    return true;
                }

                EmitInstruction(instruction, 0);
                if (Statements.Count != initialLineCount)
                    return false;
            }

            return false;
        }
        finally
        {
            RestoreSnapshot(snapshot);
        }
    }

    private bool IsLoopBackBranch(Instruction instruction, int bodyStartIndex, int endIndex, out string conditionExpression)
    {
        conditionExpression = "";
        if (instruction.Operand is not Instruction targetInstruction)
            return false;

        if (!_instructionIndices.TryGetValue(targetInstruction, out var targetIndex) || targetIndex != bodyStartIndex)
            return false;

        switch (instruction.OpCode.Code)
        {
            case Code.Brtrue:
            case Code.Brtrue_S:
                conditionExpression = MakeExplicitBooleanCheck(Pop().Code, invert: false);
                return true;
            case Code.Brfalse:
            case Code.Brfalse_S:
                conditionExpression = MakeExplicitBooleanCheck(Pop().Code, invert: true);
                return true;
            case Code.Beq:
            case Code.Beq_S:
            case Code.Bne_Un:
            case Code.Bne_Un_S:
            case Code.Bge:
            case Code.Bge_S:
            case Code.Bge_Un:
            case Code.Bge_Un_S:
            case Code.Bgt:
            case Code.Bgt_S:
            case Code.Bgt_Un:
            case Code.Bgt_Un_S:
            case Code.Ble:
            case Code.Ble_S:
            case Code.Ble_Un:
            case Code.Ble_Un_S:
            case Code.Blt:
            case Code.Blt_S:
            case Code.Blt_Un:
            case Code.Blt_Un_S:
            {
                var right = Pop();
                var left = Pop();
                conditionExpression = BuildLoopConditionForCompareBranch(instruction.OpCode.Code, left, right);
                return true;
            }
            default:
                return false;
        }
    }

    private bool TryReadLoopBackConditionFromLocalTemp(int index, int bodyStartIndex, int endIndex, out string conditionExpression)
    {
        conditionExpression = "";
        if (index + 2 >= endIndex)
            return false;

        if (!TryGetLocalIndex(_instructions[index], out var storedLocalIndex))
            return false;

        if (!TryGetLoadedLocalIndex(_instructions[index + 1], out var loadedLocalIndex) || loadedLocalIndex != storedLocalIndex)
            return false;

        if (_instructions[index + 2].Operand is not Instruction targetInstruction)
            return false;

        if (!_instructionIndices.TryGetValue(targetInstruction, out var targetIndex) || targetIndex != bodyStartIndex)
            return false;

        if (_instructions[index + 2].OpCode.Code is not (Code.Brtrue or Code.Brtrue_S or Code.Brfalse or Code.Brfalse_S))
            return false;

        conditionExpression = _instructions[index + 2].OpCode.Code is Code.Brfalse or Code.Brfalse_S ? MakeExplicitBooleanCheck(Pop().Code, invert: true) : MakeExplicitBooleanCheck(Pop().Code, invert: false);

        return true;
    }

    private int FindLoopIncrementStart(int bodyStartIndex, int conditionStartIndex)
    {
        var incrementStartIndex = conditionStartIndex;
        for (var index = bodyStartIndex; index < conditionStartIndex; index++)
        {
            var instruction = _instructions[index];
            if (instruction.OpCode.Code is not (Code.Br or Code.Br_S))
                continue;

            if (instruction.Operand is not Instruction targetInstruction)
                continue;

            if (!_instructionIndices.TryGetValue(targetInstruction, out var targetIndex))
                continue;

            if (targetIndex <= bodyStartIndex || targetIndex >= conditionStartIndex)
                continue;

            if (targetIndex > incrementStartIndex)
                continue;

            if (incrementStartIndex == conditionStartIndex || targetIndex > incrementStartIndex)
                incrementStartIndex = targetIndex;
        }

        return incrementStartIndex;
    }

    private bool TryGetElseBranch(int bodyStartIndex, int bodyEndIndex, int endIndex, out int thenEndIndex, out int elseEndIndex)
    {
        thenEndIndex = bodyEndIndex;
        elseEndIndex = bodyEndIndex;

        var finalInstructionIndex = FindLastMeaningfulInstructionIndex(bodyStartIndex, bodyEndIndex);
        if (finalInstructionIndex < bodyStartIndex)
            return false;

        var finalInstruction = _instructions[finalInstructionIndex];
        if (finalInstruction.OpCode.Code is not (Code.Br or Code.Br_S))
            return false;

        var targetInstruction = (Instruction)finalInstruction.Operand;
        if (!_instructionIndices.TryGetValue(targetInstruction, out var targetIndex) || targetIndex <= bodyEndIndex || targetIndex > endIndex)
            return false;

        thenEndIndex = finalInstructionIndex;
        elseEndIndex = targetIndex;
        return true;
    }

    private int FindLastMeaningfulInstructionIndex(int startIndex, int endIndex)
    {
        for (var index = endIndex - 1; index >= startIndex; index--)
        {
            if (_instructions[index].OpCode.Code != Code.Nop)
                return index;
        }

        return startIndex - 1;
    }

    private bool IsEffectivelyEmptyRange(int startIndex, int endIndex)
    {
        return FindLastMeaningfulInstructionIndex(startIndex, endIndex) < startIndex;
    }

    private static string NegateCondition(string condition) => $"!({condition})";

    private bool TryGetBranchTargetIndex(Instruction branchInstruction, int sourceIndex, int endIndex, out int targetIndex)
    {
        targetIndex = -1;
        var targetInstruction = (Instruction)branchInstruction.Operand;
        return _instructionIndices.TryGetValue(targetInstruction, out targetIndex) && targetIndex > sourceIndex && targetIndex <= endIndex;
    }

    private bool TryBuildReturnFromBranchTarget(Instruction targetInstruction, out string returnExpression)
    {
        returnExpression = "";
        if (!_instructionIndices.TryGetValue(targetInstruction, out var targetIndex) || targetIndex >= _instructions.Count - 1)
            return false;

        var valueInstruction = _instructions[targetIndex];
        var retInstruction = _instructions[targetIndex + 1];
        if (retInstruction.OpCode.Code != Code.Ret)
            return false;

        if (TryGetLoadedLocalIndex(valueInstruction, out var localIndex))
        {
            returnExpression = GetLocalName(localIndex);
            return true;
        }

        if (TryGetLoadedArgumentIndex(valueInstruction, out var argumentIndex))
        {
            returnExpression = GetArgumentName(argumentIndex);
            return true;
        }

        return false;
    }

    private bool TryBuildEarlyExit(int startIndex, int endIndex, out string exitStatement)
    {
        exitStatement = "";
        var finalInstructionIndex = FindLastMeaningfulInstructionIndex(startIndex, endIndex);
        if (finalInstructionIndex < startIndex)
            return false;

        if (!AreAllMeaningfulInstructionsWithinRange(startIndex, endIndex, finalInstructionIndex))
            return false;

        var instruction = _instructions[finalInstructionIndex];
        if (instruction.OpCode.Code == Code.Ret)
        {
            if (_method.ReturnType.FullName != "System.Void")
                return false;

            exitStatement = "return;";
            return true;
        }

        if (instruction.OpCode.Code is not (Code.Br or Code.Br_S or Code.Leave or Code.Leave_S))
            return false;

        var targetInstruction = (Instruction)instruction.Operand;
        if (TryBuildReturnFromBranchTarget(targetInstruction, out var returnExpression))
        {
            exitStatement = $"return {returnExpression};";
            return true;
        }

        if (_instructionIndices.TryGetValue(targetInstruction, out var targetIndex) && targetIndex < _instructions.Count && _instructions[targetIndex].OpCode.Code == Code.Ret)
        {
            exitStatement = "return;";
            return _method.ReturnType.FullName == "System.Void";
        }

        return false;
    }

    private bool AreAllMeaningfulInstructionsWithinRange(int startIndex, int endIndex, params int[] allowedIndices)
    {
        var allowed = new HashSet<int>(allowedIndices);
        for (var index = startIndex; index < endIndex; index++)
        {
            if (_instructions[index].OpCode.Code == Code.Nop)
                continue;

            if (!allowed.Contains(index))
                return false;
        }

        return true;
    }
}
