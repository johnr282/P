using PChecker.Runtime.Exceptions;
using Plang.Compiler.TypeChecker.AST.Expressions;
using Plang.Compiler.TypeChecker.AST.Statements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution
{
    internal static class Execution
    {
        internal static ExecutionResult Step(SymState startState)
        {
            var state = SymState.Clone(startState);

            return state.Control switch
            {
                StmtControl control => StepStmt(state, control),
                RValueControl control => StepRValue(state, control),
                LValueControl control => StepLValue(state, control),
                _ => throw new PInternalException("Unknown symbolic control.")
            };
        }

        internal static ExecutionResult StepStmt(
            SymState state, 
            StmtControl control)
        {
            return control.Stmt switch
            {
               AssignStmt assignStmt => StepAssignStmt(state, assignStmt, control.Continuation),
                _ => throw new PInternalException(
                        $"Unrecognized statement type: '{control.Stmt?.GetType().FullName}'.")
            };
        }

        internal static ExecutionResult StepAssignStmt(
            SymState state,
            AssignStmt assignStmt, 
            StmtContinuation continuation)
        {
            state.Control = new LValueControl(
                assignStmt.Location,
                new AssignLocationContinuation(assignStmt.Value, continuation));

            return new SingleSuccessor(state);
        }

        internal static ExecutionResult StepRValue(
            SymState state, 
            RValueControl control)
        {
            throw new NotImplementedException();
        }

        internal static ExecutionResult StepLValue(
            SymState state, 
            LValueControl control)
        {
            return control.Location switch
            {
                VariableAccessExpr expr => StepVariableAccessExpr(state, expr, control.Continuation),
                _ => throw new PInternalException(
                        $"Unrecognized lvalue expression type: '{control.Location?.GetType().FullName}'.")
            };
        }

        private static ExecutionResult StepVariableAccessExpr(
            SymState state,
            VariableAccessExpr expr,
            LValueContinuation continuation)
        {
            throw new NotImplementedException();
        }
    }
}
