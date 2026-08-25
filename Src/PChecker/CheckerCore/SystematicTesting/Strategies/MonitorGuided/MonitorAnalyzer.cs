using PChecker.Configuration;
using Plang.Compiler;
using PChecker.Runtime.Exceptions;
using PChecker.IO.Debugging;
using Plang.Compiler.TypeChecker.AST.Declarations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PChecker.Runtime.Specifications;
using Plang.Compiler.Backend.PEx;
using Plang.Compiler.TypeChecker.AST.States;
using Plang.Compiler.TypeChecker.AST;
using Plang.Compiler.TypeChecker.AST.Statements;


namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    internal class MonitorAnalyzer
    {
        private readonly Machine monitorAST;
        
        private readonly IFrontier<ExplorationNode> frontier;

        private readonly uint maxObservedEvents;

        private ExplorationNode violatingNode = null;

        /// <summary>
        /// Constructs a new instance of the <see cref="MonitorAnalyzer"/> class."/>
        /// </summary>
        /// <param name="monitor">
        /// Typed AST of the monitor state machine to analyze.
        /// </param>
        /// <param name="maxEvents">
        /// Maximum observed events in a single explored monitor execution.
        /// </param>
        /// <param name="strategy">
        /// Search strategy used to explore monitor executions.
        /// </param>
        internal MonitorAnalyzer(
            Machine monitor,
            uint maxEvents,
            SearchStrategy strategy)
        {
            monitorAST = monitor;
            maxObservedEvents = maxEvents;

            frontier = strategy switch
            {
                SearchStrategy.BFS => new QueueFrontier<ExplorationNode>(),
                SearchStrategy.DFS => new StackFrontier<ExplorationNode>(),
                _ => throw new ArgumentException(
                    $"Unsupported MonitorAnalyzer search strategy: {strategy}")
            };
        }

        /// <summary>
        /// Attempts to find a sequence of events resulting in a monitor assertion failure.
        /// </summary>
        /// <param name="currentState">
        /// Current state of the monitor state machine.
        /// </param>
        /// <param name="concreteGlobals">
        /// Current concrete values of monitor global variables.
        /// </param>
        /// <param name="violatingExecution">Violating sequence of events.</param>
        /// <returns>True if a violating execution was found, false otherwise.</returns>
        internal bool FindViolatingExecution(
            State currentState,
            Dictionary<string, object> concreteGlobals, 
            out List<SymEvent> violatingExecution)
        {
            // Construct initial exploration and place in frontier

            while (frontier.TryRemoveNext(out var nextNode) &&
                violatingNode == null)
            {
                ComputeSuccessorNodes(nextNode);
            }

            if (violatingNode == null)
            {
                violatingExecution = null;
                return false;
            }

            violatingExecution = ComputeCausalExecution(violatingNode);
            return true;
        }

        /// <summary>
        /// Compute successor nodes of the given node and add them to the frontier.
        /// </summary>
        private void ComputeSuccessorNodes(ExplorationNode node)
        {
            SymState state = node.SymbolicState;
            if (!state.IsHandlingEvent)
            {
                if (state.ObservedEvents >= maxObservedEvents)
                {
                    return;
                }

                var availableHandlers = state.CurrentState.AllEventHandlers;
                foreach (var handler in availableHandlers)
                {
                    var nextState = SymState.Clone(state);

                    // Create new stack frame for the handler, adding a symbolic
                    // payload local variable for the event parameter, and add
                    // it to nextState.CallStack
                    nextState.ObservedEvents++;

                    // TransitionEvent is symbolic event used for event parameter
                    SymEvent transitionEvent = null;
                    var nextNode = new ExplorationNode(
                        nextState,
                        node,
                        transitionEvent);
                    frontier.Add(nextNode);
                }

                return;
            }
            
            // Execute current event handler until a state has multiple feasible
            // successors, which occurs when either a branch statement is reached,
            // or the handler completes and a new observed event is needed to
            // continue execution. 
            while (true)
            {
                var result = ExecuteNextStatement(state, out var successors);

                switch (result)
                {
                    case ExecutionResult.Terminated:
                        return;
                    case ExecutionResult.Violation:
                        violatingNode = new ExplorationNode(
                            state,
                            node,
                            null);
                        return;
                    case ExecutionResult.HasSuccessors:
                        if (successors.Count > 1)
                        {
                            foreach (var successor in successors)
                            {
                                var nextNode = new ExplorationNode(
                                    successor,
                                    node,
                                    null);
                                frontier.Add(nextNode);
                            }
                            return;
                        }

                        state = successors[0];
                        if (!state.IsHandlingEvent)
                        {
                            // Current event handler has completed
                            var nextNode = new ExplorationNode(
                                state,
                                node,
                                null);
                            frontier.Add(nextNode);
                            return;
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Symbolically executes the next statement according to the given 
        /// symbolic state. 
        /// </summary>
        /// <param name="state">State to begin execution from.</param>
        /// <param name="successors">
        /// Successor states resulting from execution.
        /// </param>
        /// <returns>
        /// Result of execution. 
        /// </returns>
        private ExecutionResult ExecuteNextStatement(
            SymState state,
            out IReadOnlyList<SymState> successors)
        {
            var frame = state.CallStack.Peek();
            var nextStatement = GetNextStatement(frame);

            switch (nextStatement)
            {
                case AddStmt stmt:      return StatementExecutor.ExecuteAddStmt(state, stmt, out successors);
                case AssertStmt stmt:   return StatementExecutor.ExecuteAssertStmt(state, stmt, out successors);
                case AssignStmt stmt:   return StatementExecutor.ExecuteAssignStmt(state, stmt, out successors);
                case BreakStmt stmt:    return StatementExecutor.ExecuteBreakStmt(state, stmt, out successors);
                case CompoundStmt stmt: return StatementExecutor.ExecuteCompoundStmt(state, stmt, out successors);
                case ContinueStmt stmt: return StatementExecutor.ExecuteContinueStmt(state, stmt, out successors);
                case ForeachStmt stmt:  return StatementExecutor.ExecuteForeachStmt(state, stmt, out successors);
                case FunCallStmt stmt:  return StatementExecutor.ExecuteFunCallStmt(state, stmt, out successors);
                case GotoStmt stmt:     return StatementExecutor.ExecuteGotoStmt(state, stmt, out successors);
                case IfStmt stmt:       return StatementExecutor.ExecuteIfStmt(state, stmt, out successors);
                case InsertStmt stmt:   return StatementExecutor.ExecuteInsertStmt(state, stmt, out successors);
                case NoStmt stmt:       return StatementExecutor.ExecuteNoStmt(state, stmt, out successors);
                case PrintStmt stmt:    return StatementExecutor.ExecutePrintStmt(state, stmt, out successors);
                case RaiseStmt stmt:    return StatementExecutor.ExecuteRaiseStmt(state, stmt, out successors);
                case RemoveStmt stmt:   return StatementExecutor.ExecuteRemoveStmt(state, stmt, out successors);
                case ReturnStmt stmt:   return StatementExecutor.ExecuteReturnStmt(state, stmt, out successors);
                case WhileStmt stmt:    return StatementExecutor.ExecuteWhileStmt(state, stmt, out successors);

                case AnnounceStmt:
                case AssumeStmt:
                case CtorStmt:
                case MoveAssignStmt:
                case ReceiveStmt:
                case SendStmt:
                case SwapAssignStmt:
                case ReceiveSplitStmt: 
                    throw new PInternalException(
                        $"Unsupported monitor statement type: '{nextStatement.GetType().Name}'.");
                default:
                    throw new PInternalException(
                        $"Unrecognized statement type: '{nextStatement?.GetType().FullName}'.");
            }
        }

        private IPStmt GetNextStatement(StackFrame frame)
        {
            var function = monitorAST.Methods.FirstOrDefault(
                m => m.Name.Equals(frame.FunctionName));

            if (function == null)
            {
                throw new PInternalException(
                    $"Function '{frame.FunctionName}' not found in monitor AST.");
            }

            return function.Body.Statements[frame.ProgramCounter];
        }

        /// <summary>
        /// Computes sequence of events leading to the given node.
        /// </summary>
        private List<SymEvent> ComputeCausalExecution(ExplorationNode node)
        {
            List<SymEvent> execution = new();

            while (node != null)
            {
                if (node.TransitionEvent != null)
                {
                    execution.Add(node.TransitionEvent);
                }
                node = node.Parent;
            }

            execution.Reverse();
            return execution;
        }

        internal enum SearchStrategy
        {
            BFS,
            DFS
        }

        internal enum ExecutionResult
        {
            HasSuccessors,
            Terminated,
            Violation
        }

        private sealed class ExplorationNode
        {
            public SymState SymbolicState { get; }
            public ExplorationNode Parent { get; }
            public SymEvent TransitionEvent { get; }

            public ExplorationNode(
                SymState symbolicState,
                ExplorationNode parent,
                SymEvent transitionEvent)
            {
                SymbolicState = symbolicState;
                Parent = parent;
                TransitionEvent = transitionEvent;
            }
        }

        /// <summary>
        /// Performs parsing and type checking of the P program to extract the 
        /// monitor ASTs.
        /// </summary>
        internal static void CompileMonitors(CheckerConfiguration configuration)
        {
            if (!Compiler.ParseAndTypeCheck(configuration.CompilerConfig, out var scope))
            {
                Error.CheckerReportAndExit(
                    "Parsing and type checking during monitor analysis for " +
                    "monitor-guided strategy failed.");
                return;
            }

            configuration.MonitorASTs = scope.Machines.Where(m => m.IsSpec).ToList();
        }

        /// <summary>
        /// Returns the monitor AST from the given configuration with the given name.
        /// </summary>
        internal static Machine GetCorrespondingMonitorAST(
            CheckerConfiguration configuration, 
            string monitorASTName)
        {
            var matchingASTs = configuration.MonitorASTs
                .Where(m => m.Name.Equals(monitorASTName)).ToList();

            if (matchingASTs.Count == 0)
            {
                Error.ReportAndExit(
                    $"No monitor AST found for monitor '{monitorASTName}'.");
            }
            else if (matchingASTs.Count > 1)
            {
                Error.ReportAndExit(
                    $"Multiple monitor ASTs found for monitor '{monitorASTName}'.");
            }

            return matchingASTs[0];
        }
    }
}
