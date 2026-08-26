using PChecker.Runtime.Exceptions;
using Plang.Compiler.Backend.PEx;
using Plang.Compiler.TypeChecker.AST;
using Plang.Compiler.TypeChecker.AST.Statements;
using System;
using System.Collections.Generic;


namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution
{
    internal static class StatementExecutor
    {
        internal static ExecutionResult ExecuteStmt(
            SymState startState,
            IPStmt statement)
        {
            switch (statement)
            {
                case AddStmt stmt: return StatementExecutor.ExecuteAddStmt(state, stmt, out successors);
                case AssertStmt stmt: return StatementExecutor.ExecuteAssertStmt(state, stmt, out successors);
                case AssignStmt stmt: return StatementExecutor.ExecuteAssignStmt(state, stmt, out successors);
                case BreakStmt stmt: return StatementExecutor.ExecuteBreakStmt(state, stmt, out successors);
                case CompoundStmt stmt: return StatementExecutor.ExecuteCompoundStmt(state, stmt, out successors);
                case ContinueStmt stmt: return StatementExecutor.ExecuteContinueStmt(state, stmt, out successors);
                case ForeachStmt stmt: return StatementExecutor.ExecuteForeachStmt(state, stmt, out successors);
                case FunCallStmt stmt: return StatementExecutor.ExecuteFunCallStmt(state, stmt, out successors);
                case GotoStmt stmt: return StatementExecutor.ExecuteGotoStmt(state, stmt, out successors);
                case IfStmt stmt: return StatementExecutor.ExecuteIfStmt(state, stmt, out successors);
                case InsertStmt stmt: return StatementExecutor.ExecuteInsertStmt(state, stmt, out successors);
                case NoStmt stmt: return StatementExecutor.ExecuteNoStmt(state, stmt, out successors);
                case PrintStmt stmt: return StatementExecutor.ExecutePrintStmt(state, stmt, out successors);
                case RaiseStmt stmt: return StatementExecutor.ExecuteRaiseStmt(state, stmt, out successors);
                case RemoveStmt stmt: return StatementExecutor.ExecuteRemoveStmt(state, stmt, out successors);
                case ReturnStmt stmt: return StatementExecutor.ExecuteReturnStmt(state, stmt, out successors);
                case WhileStmt stmt: return StatementExecutor.ExecuteWhileStmt(state, stmt, out successors);

                case AnnounceStmt:
                case AssumeStmt:
                case CtorStmt:
                case MoveAssignStmt:
                case ReceiveStmt:
                case SendStmt:
                case SwapAssignStmt:
                case ReceiveSplitStmt:
                    throw new PInternalException(
                        $"Unsupported monitor statement type: '{statement.GetType().Name}'.");
                default:
                    throw new PInternalException(
                        $"Unrecognized statement type: '{statement?.GetType().FullName}'.");
            }
        }
        
        internal static ExecutionResult ExecuteAddStmt(
            SymState startState, 
            AddStmt addStmt, 
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecuteAssertStmt(
            SymState startState,
            AssertStmt assertStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecuteAssignStmt(
            SymState startState,
            AssignStmt assignStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecuteBreakStmt(
            SymState startState,
            BreakStmt breakStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecuteCompoundStmt(
            SymState startState,
            CompoundStmt compoundStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecuteContinueStmt(
            SymState startState,
            ContinueStmt continueStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecuteForeachStmt(
            SymState startState,
            ForeachStmt foreachStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecuteFunCallStmt(
            SymState startState,
            FunCallStmt funCallStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecuteGotoStmt(
            SymState startState,
            GotoStmt gotoStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecuteIfStmt(
            SymState startState,
            IfStmt ifStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecuteInsertStmt(
            SymState startState,
            InsertStmt insertStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecuteNoStmt(
            SymState startState,
            NoStmt noStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecutePrintStmt(
            SymState startState,
            PrintStmt printStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecuteRaiseStmt(
            SymState startState,
            RaiseStmt raiseStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecuteRemoveStmt(
            SymState startState,
            RemoveStmt removeStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecuteReturnStmt(
            SymState startState,
            ReturnStmt returnStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult ExecuteWhileStmt(
            SymState startState,
            WhileStmt whileStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }
    }
}
