using Plang.Compiler.TypeChecker.AST;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution
{
    /// <summary>
    /// Continuation after a statement's execution has finished.
    /// </summary>
    internal abstract record StmtContinuation;

    /// <summary>
    /// Continuation after an rvalue is evaluated.
    /// </summary>
    internal abstract record RValueContinuation;

    /// <summary>
    /// Value of assign has been resolved; now assign it to Location.
    /// </summary>
    internal sealed record AssignValueContinuation(
        ResolvedLValue Location,
        StmtContinuation Next) : RValueContinuation;

    /// <summary>
    /// Continuation after an lvalue is resolved.
    /// </summary>
    internal abstract record LValueContinuation;

    /// <summary>
    /// Location of assign has been resolved; now evaluate Value.
    /// </summary>
    internal sealed record AssignLocationContinuation(
        IPExpr Value,
        StmtContinuation Next) : LValueContinuation;
}
