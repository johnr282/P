using Plang.Compiler.TypeChecker.AST.Statements;
using System;
using System.Collections.Generic;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    internal static class StatementExecutor
    {
        internal static MonitorAnalyzer.ExecutionResult ExecuteAddStmt(
            SymState startState, 
            AddStmt addStmt, 
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static MonitorAnalyzer.ExecutionResult ExecuteAssertStmt(
            SymState startState,
            AssertStmt assertStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static MonitorAnalyzer.ExecutionResult ExecuteAssignStmt(
            SymState startState,
            AssignStmt assignStmt,
            out IReadOnlyList<SymState> successors)
        {
            
        }

        internal static MonitorAnalyzer.ExecutionResult ExecuteBreakStmt(
            SymState startState,
            BreakStmt breakStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static MonitorAnalyzer.ExecutionResult ExecuteCompoundStmt(
            SymState startState,
            CompoundStmt compoundStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static MonitorAnalyzer.ExecutionResult ExecuteContinueStmt(
            SymState startState,
            ContinueStmt continueStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static MonitorAnalyzer.ExecutionResult ExecuteForeachStmt(
            SymState startState,
            ForeachStmt foreachStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static MonitorAnalyzer.ExecutionResult ExecuteFunCallStmt(
            SymState startState,
            FunCallStmt funCallStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static MonitorAnalyzer.ExecutionResult ExecuteGotoStmt(
            SymState startState,
            GotoStmt gotoStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static MonitorAnalyzer.ExecutionResult ExecuteIfStmt(
            SymState startState,
            IfStmt ifStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static MonitorAnalyzer.ExecutionResult ExecuteInsertStmt(
            SymState startState,
            InsertStmt insertStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static MonitorAnalyzer.ExecutionResult ExecuteNoStmt(
            SymState startState,
            NoStmt noStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static MonitorAnalyzer.ExecutionResult ExecutePrintStmt(
            SymState startState,
            PrintStmt printStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static MonitorAnalyzer.ExecutionResult ExecuteRaiseStmt(
            SymState startState,
            RaiseStmt raiseStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static MonitorAnalyzer.ExecutionResult ExecuteRemoveStmt(
            SymState startState,
            RemoveStmt removeStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static MonitorAnalyzer.ExecutionResult ExecuteReturnStmt(
            SymState startState,
            ReturnStmt returnStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }

        internal static MonitorAnalyzer.ExecutionResult ExecuteWhileStmt(
            SymState startState,
            WhileStmt whileStmt,
            out IReadOnlyList<SymState> successors)
        {
            throw new NotImplementedException();
        }
    }
}
