using PChecker.Runtime.Exceptions;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Continuations;
using Plang.Compiler.TypeChecker.AST.Declarations;
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
                NamedTupleAccessExpr expr => StepNamedTupleAccessExpr(state, expr, control.Continuation),
                TupleAccessExpr expr => StepTupleAccessExpr(state, expr, control.Continuation),
                _ => throw new PInternalException(
                        $"Unrecognized lvalue expression type: '{control.Location?.GetType().FullName}'.")
            };
        }

        /// <summary>
        /// Completes the resolution of lvalue location by updating the state's
        /// control according to the given continuation.
        /// </summary>
        private static ExecutionResult CompleteLValue(
            SymState state,
            ResolvedLValue location,
            LValueContinuation continuation)
        {
            switch (continuation)
            {
                case AssignLocationContinuation assign:
                    state.Control = new RValueControl(
                        assign.Value,
                        new AssignValueContinuation(location, assign.Next));
                    return new SingleSuccessor(state);

                case NamedTupleFieldContinuation field:
                    return CompleteLValue(
                        state,
                        new NamedTupleFieldLValue(location, field.Entry),
                        field.Next);

                case TupleFieldContinuation field:
                    return CompleteLValue(
                        state,
                        new TupleFieldLValue(location, field.FieldNo, field.FieldType),
                        field.Next);

                default:
                    throw new PInternalException(
                        $"Unrecognized lvalue continuation: '{continuation?.GetType().FullName}'.");
            }
        }

        /// <summary>
        /// A variable access cannot be resolved further, so completes the lvalue.
        /// </summary>
        private static ExecutionResult StepVariableAccessExpr(
            SymState state,
            VariableAccessExpr expr,
            LValueContinuation continuation)
        {
            var storage = expr.Variable.Role switch
            {
                VariableRole.Field => VariableStorage.Global,
                VariableRole.Local or VariableRole.Param or VariableRole.Temp =>
                    VariableStorage.Local,
                _ => throw new PInternalException(
                    $"Unsupported variable role '{expr.Variable.Role}' for assignment location " +
                    $"'{expr.Variable.Name}'.")
            };

            return CompleteLValue(
                state,
                new VariableLValue(expr.Variable, storage),
                continuation);
        }

        /// <summary>
        /// Resolves the base lvalue before adding its named-field projection.
        /// </summary>
        private static ExecutionResult StepNamedTupleAccessExpr(
            SymState state,
            NamedTupleAccessExpr expr,
            LValueContinuation continuation)
        {
            state.Control = new LValueControl(
                expr.SubExpr,
                new NamedTupleFieldContinuation(expr.Entry, continuation));

            return new SingleSuccessor(state);
        }

        /// <summary>
        /// Resolves the base lvalue before adding its positional-field projection.
        /// </summary>
        private static ExecutionResult StepTupleAccessExpr(
            SymState state,
            TupleAccessExpr expr,
            LValueContinuation continuation)
        {
            state.Control = new LValueControl(
                expr.SubExpr,
                new TupleFieldContinuation(expr.FieldNo, expr.Type, continuation));

            return new SingleSuccessor(state);
        }
    }
}
