using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Continuations;
using Plang.Compiler.TypeChecker.AST;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution
{
    /// <summary>
    /// Represents the current in-progress work of the symbolic executor and its
    /// next steps.
    /// </summary>
    internal abstract record Control;

    /// <summary>
    /// Executor has no current work and is waiting to observe an event.
    /// </summary>
    internal sealed record WaitingForEventControl : Control;

    /// <summary>
    /// Statement Stmt is being executed; once finished, Continuation defines the
    /// executor's next step.
    /// </summary>
    internal sealed record StmtControl(
        IPStmt Stmt,
        StmtContinuation Continuation) : Control;

    /// <summary>
    /// rvalue Value is being evaluated; once finished, Continuation defines the
    /// executor's next step.
    /// </summary>
    internal sealed record RValueControl(
        IPExpr Value,
        RValueContinuation Continuation) : Control;

    /// <summary>
    /// lvalue Location is being resolved; once finished, Continuation defines 
    /// the executor's next step.
    /// </summary>
    internal sealed record LValueControl(
        IPExpr Location,
        LValueContinuation Continuation) : Control;
}
