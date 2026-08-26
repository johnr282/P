using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Execution;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution
{
    internal abstract record ExecutionResult;

    internal sealed record SingleSuccessor(
        SymState Successor) : ExecutionResult;

    internal sealed record MultipleSuccessors(
        IReadOnlyList<SymState> Successors) : ExecutionResult;

    internal sealed record Violation(
        SymState State) : ExecutionResult;

    internal sealed record Terminated : ExecutionResult;
}
