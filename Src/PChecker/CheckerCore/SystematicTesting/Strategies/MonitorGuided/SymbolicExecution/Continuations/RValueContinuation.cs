using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Continuations
{
    /// <summary>
    /// Continuation after an rvalue is evaluated.
    /// </summary>
    internal abstract record RValueContinuation;

    /// <summary>
    /// Value of assign is being evaluated; next, assign it to Location.
    /// </summary>
    internal sealed record AssignValueContinuation(
        ResolvedLValue Location,
        StmtContinuation Next) : RValueContinuation;
}
