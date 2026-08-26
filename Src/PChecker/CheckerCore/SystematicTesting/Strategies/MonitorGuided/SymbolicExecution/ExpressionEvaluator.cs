using PChecker.Runtime.Exceptions;
using Plang.Compiler.TypeChecker.AST;
using Plang.Compiler.TypeChecker.AST.Expressions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution
{
    internal static class ExpressionEvaluator
    {
        internal sealed record RValueResult(
            SymState State,
            SymValue Value);

        internal sealed record LValueResult(
            SymState State,
            ResolvedLValue Location);

        internal static IReadOnlyList<LValueResult> ResolveLValue(
            SymState startState, 
            IPExpr lvalue)
        {
            switch (lvalue)
            {
                case VariableAccessExpr expr:   return ResolveVariableAccessExpr(startState, expr);
                case MapAccessExpr expr:        return ResolveMapAccessExpr(startState, expr);
                case SeqAccessExpr expr:        return ResolveSeqAccessExpr(startState, expr);
                case SetAccessExpr expr:        return ResolveSetAccessExpr(startState, expr);
                case NamedTupleAccessExpr expr: return ResolveNamedTupleAccessExpr(startState, expr);
                case TupleAccessExpr expr:      return ResolveTupleAccessExpr(startState, expr);
                default:
                    throw new PInternalException(
                        $"Unexpected lvalue expression type: {lvalue.GetType().Name}");
            }
        }

        private static IReadOnlyList<LValueResult> ResolveVariableAccessExpr(
            SymState startState,
            VariableAccessExpr expr)
        {
            throw new NotImplementedException();
        }

        private static IReadOnlyList<LValueResult> ResolveMapAccessExpr(
            SymState startState,
            MapAccessExpr expr)
        {
            throw new NotImplementedException();
        }

        private static IReadOnlyList<LValueResult> ResolveSeqAccessExpr(
            SymState startState,
            SeqAccessExpr expr)
        {
            throw new NotImplementedException();
        }

        private static IReadOnlyList<LValueResult> ResolveSetAccessExpr(
            SymState startState,
            SetAccessExpr expr)
        {
            throw new NotImplementedException();
        }

        private static IReadOnlyList<LValueResult> ResolveNamedTupleAccessExpr(
            SymState startState,
            NamedTupleAccessExpr expr)
        {
            throw new NotImplementedException();
        }

        private static IReadOnlyList<LValueResult> ResolveTupleAccessExpr(
            SymState startState,
            TupleAccessExpr expr)
        {
            throw new NotImplementedException();
        }

        internal static IReadOnlyList<RValueResult> EvaluateRValue(
            SymState startState, 
            IPExpr rvalue)
        {

        }

        
    }
}
