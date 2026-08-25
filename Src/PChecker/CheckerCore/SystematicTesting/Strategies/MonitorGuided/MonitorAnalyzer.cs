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
using Plang.Compiler.TypeChecker.AST.States;
using Plang.Compiler.TypeChecker.AST;


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
            out List<SymbolicEvent> violatingExecution)
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
            SymbolicState state = node.SymbolicState;
            if (!state.IsHandlingEvent)
            {
                if (state.ObservedEvents >= maxObservedEvents)
                {
                    return;
                }

                var availableHandlers = state.CurrentState.AllEventHandlers;
                foreach (var handler in availableHandlers)
                {
                    var nextState = state;

                    // Create new stack frame for the handler, adding a symbolic
                    // payload local variable for the event parameter, and add
                    // it to nextState.CallStack

                    // TransitionEvent is symbolic event used for event parameter
                    SymbolicEvent transitionEvent = null;
                    var nextNode = new ExplorationNode(
                        nextState,
                        node,
                        transitionEvent);
                    frontier.Add(nextNode);
                }
            }
            else
            {
                // Execute current event handler until a state has multiple feasible
                // successors, which occurs when either a branch statement is reached,
                // or the handler completes and a new observed event is needed to
                // continue execution. 
                while (true)
                {
                    var result = ExecuteNextStatement(state, out var successors);
                    if (result == ExecutionResult.Violation)
                    {
                        violatingNode = new ExplorationNode(
                            state,
                            node,
                            null);
                        return;
                    }

                    if (result == ExecutionResult.Terminated)
                    {
                        return;
                    }

                    if (successors.Count() > 1)
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

                    state = successors.First();
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
            SymbolicState state,
            out IEnumerable<SymbolicState> successors)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Computes sequence of events leading to the given node.
        /// </summary>
        private List<SymbolicEvent> ComputeCausalExecution(ExplorationNode node)
        {
            List<SymbolicEvent> execution = new();

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

        private enum ExecutionResult
        {
            Successors,
            Terminated,
            Violation
        }

        private class ExplorationNode
        {
            public SymbolicState SymbolicState { get; }
            public ExplorationNode Parent { get; }
            public SymbolicEvent TransitionEvent { get; }

            public ExplorationNode(
                SymbolicState symbolicState,
                ExplorationNode parent,
                SymbolicEvent transitionEvent)
            {
                SymbolicState = symbolicState;
                Parent = parent;
                TransitionEvent = transitionEvent;
            }
        }

        internal class SymbolicEvent
        {
        }

        private class SymbolicState
        {
            public State CurrentState { get; }
            public Stack<StackFrame> CallStack { get; }
            public Dictionary<string, SymbolicValue> Globals { get; }
            public PathCondition PathCondition { get; }
            public uint ObservedEvents { get; set; }

            public SymbolicState(
                State currentState,
                Stack<StackFrame> callStack,
                Dictionary<string, SymbolicValue> globals,
                PathCondition pathCondition,
                uint observedEvents)
            {
                CurrentState = currentState;
                CallStack = callStack;
                Globals = globals;
                PathCondition = pathCondition;
                ObservedEvents = observedEvents;
            }

            /// <summary>
            /// Whether a monitor in this state is handling an event.
            /// </summary>
            public bool IsHandlingEvent => CallStack.TryPeek(out var _);
        }

        private class StackFrame
        {
            public Dictionary<string, SymbolicValue> Locals { get; }
            public ProgramCounter ProgramCounter { get; }
        }

        private class ProgramCounter
        { 
        }

        private class SymbolicValue
        {
        }

        private class PathCondition
        {
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
