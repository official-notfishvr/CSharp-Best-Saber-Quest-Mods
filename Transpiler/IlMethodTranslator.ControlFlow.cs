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

            if (TryEmitStructuredIfFromLocalTemp(index, endIndex, indentLevel, out var consumedUntil))
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
        var positiveCondition = branch.OpCode.Code is Code.Brfalse or Code.Brfalse_S ? Pop().Code : NegateCondition(Pop().Code);
        positiveCondition = ExtendConditionChain(positiveCondition, ref bodyStartIndex, targetIndex, endIndex);
        return TryEmitStructuredConditionalBlock(positiveCondition, bodyStartIndex, targetIndex, endIndex, indentLevel, out consumedUntil);
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
                var positiveCondition = instruction.OpCode.Code is Code.Brfalse or Code.Brfalse_S ? Pop().Code : NegateCondition(Pop().Code);
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
                var positiveCondition = BuildBodyConditionForCompareBranch(instruction.OpCode.Code, left.Code, right.Code);
                positiveCondition = ExtendConditionChain(positiveCondition, ref bodyStartIndex, targetIndex, endIndex);
                return TryEmitStructuredConditionalBlock(positiveCondition, bodyStartIndex, targetIndex, endIndex, indentLevel, out consumedUntil);
            }
            default:
                return false;
        }
    }

    private bool TryEmitStructuredConditionalBlock(string positiveCondition, int bodyStartIndex, int bodyEndIndex, int endIndex, int indentLevel, out int consumedUntil)
    {
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

    private static string BuildBodyConditionForCompareBranch(Code opcode, string left, string right)
    {
        return opcode switch
        {
            Code.Beq or Code.Beq_S => $"({left} != {right})",
            Code.Bne_Un or Code.Bne_Un_S => $"({left} == {right})",
            Code.Bge or Code.Bge_S or Code.Bge_Un or Code.Bge_Un_S => $"({left} < {right})",
            Code.Bgt or Code.Bgt_S or Code.Bgt_Un or Code.Bgt_Un_S => $"({left} <= {right})",
            Code.Ble or Code.Ble_S or Code.Ble_Un or Code.Ble_Un_S => $"({left} > {right})",
            Code.Blt or Code.Blt_S or Code.Blt_Un or Code.Blt_Un_S => $"({left} >= {right})",
            _ => throw new NotSupportedException($"Unsupported compare branch opcode {opcode}"),
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

                positiveCondition = instruction.OpCode.Code is Code.Brfalse or Code.Brfalse_S ? Pop().Code : NegateCondition(Pop().Code);
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
                positiveCondition = BuildBodyConditionForCompareBranch(instruction.OpCode.Code, left.Code, right.Code);
                return true;
            }
            default:
                return false;
        }
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
}
