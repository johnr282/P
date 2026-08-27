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
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution;
using System.Diagnostics;
using PChecker.Runtime.Values;
using Plang.Compiler.TypeChecker.Types;
using System.Collections.Immutable;


namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    internal class MonitorAnalyzer
    {
        private readonly Machine _monitorAST;
        
        private readonly IFrontier<ExplorationNode> _frontier;

        private readonly uint _maxObservedEvents;

        private ExplorationNode _violatingNode = null;

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
            _monitorAST = monitor;
            _maxObservedEvents = maxEvents;

            _frontier = strategy switch
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
        /// <param name="concreteFields">
        /// Current concrete values of monitor fields.
        /// </param>
        /// <param name="violatingExecution">Violating sequence of events.</param>
        /// <returns>True if a violating execution was found, false otherwise.</returns>
        internal bool FindViolatingExecution(
            State currentState,
            IReadOnlyDictionary<string, IPValue> concreteFields, 
            out List<SymEvent> violatingExecution)
        {
            ResetAnalyzer();

            var initialState = ConstructInitialState(currentState, concreteFields);
            var initialNode = new ExplorationNode(initialState, null, null);
            _frontier.Add(initialNode);

            while (_frontier.TryRemoveNext(out var nextNode) &&
                _violatingNode == null)
            {
                ComputeSuccessorNodes(nextNode);
            }

            if (_violatingNode == null)
            {
                violatingExecution = null;
                return false;
            }

            violatingExecution = ComputeCausalExecution(_violatingNode);
            return true;
        }

        /// <summary>
        /// Resets per-analysis analyzer state. 
        /// </summary>
        private void ResetAnalyzer()
        {
            _frontier.Clear();
            _violatingNode = null;
        }

        /// <summary>
        /// Returns the initial symbolic state of the monitor based on the 
        /// current state and concrete monitor field values.
        /// </summary>
        private SymState ConstructInitialState(
            State currentState, 
            IReadOnlyDictionary<string, IPValue> concreteFields)
        {
            Dictionary<string, SymExpr> symFields = new();

            foreach (var field in _monitorAST.Fields)
            {
                if (!concreteFields.TryGetValue(field.Name, out var value))
                {
                    throw new PInternalException(
                        $"Missing concrete value for monitor field '{field.Name}'");
                }
                symFields[field.Name] = new ConcreteExpr(value, field.Type);
            }

            return new SymState(
                currentState,
                new Stack<SymbolicExecution.StackFrame>(),
                new WaitingForEventControl(),
                symFields,
                new PathCondition(),
                0);
        }

        /// <summary>
        /// Compute successor nodes of the given node and add them to the frontier.
        /// </summary>
        private void ComputeSuccessorNodes(ExplorationNode node)
        {
            SymState state = node.SymbolicState;
            if (state.WaitingForEvent)
            {
                if (state.ObservedEvents >= _maxObservedEvents)
                {
                    return;
                }

                var availableHandlers = state.CurrentState.AllEventHandlers;
                foreach (var handler in availableHandlers)
                {
                    var nextState = SymState.Clone(state);

                    // JR TODO: Create new stack frame for the handler, adding a symbolic
                    // payload local variable for the event parameter, and add
                    // it to nextState.CallStack
                    Event eventType = handler.Key;
                    IStateAction handlerAction = handler.Value;


                    nextState.ObservedEvents++;

                    // JR TODO: TransitionEvent is symbolic event used for event parameter
                    SymEvent transitionEvent = null;
                    var nextNode = new ExplorationNode(
                        nextState,
                        node,
                        transitionEvent);
                    _frontier.Add(nextNode);
                }

                return;
            }
            
            // Execute current event handler until a state has multiple feasible
            // successors, which occurs when either a branch statement is reached,
            // or the handler completes and a new observed event is needed to
            // continue execution. 
            while (true)
            {
                var result = Execution.Step(state);

                switch (result)
                {
                    case Terminated:
                        return;

                    case Violation violation:
                        _violatingNode = new ExplorationNode(
                            violation.State,
                            node,
                            null);
                        return;

                    case SingleSuccessor successor:
                        state = successor.Successor;
                        if (state.WaitingForEvent)
                        {
                            // Current event handler has completed
                            var nextNode = new ExplorationNode(
                                state,
                                node,
                                null);
                            _frontier.Add(nextNode);
                            return;
                        }
                        break;

                    case MultipleSuccessors successors:
                        foreach (var successor in successors.Successors)
                        {
                            var nextNode = new ExplorationNode(
                                successor,
                                node,
                                null);
                            _frontier.Add(nextNode);
                        }
                        return;
                }
            }
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
    }
}
