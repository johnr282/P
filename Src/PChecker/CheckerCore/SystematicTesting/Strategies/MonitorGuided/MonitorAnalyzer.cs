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

using RuntimeEvent = PChecker.Runtime.Events.Event;
using ASTEvent = Plang.Compiler.TypeChecker.AST.Declarations.Event;


namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    internal class MonitorAnalyzer
    {
        private readonly Machine _monitorAST;

        private readonly IMonitorFieldProvider _fieldProvider;

        private readonly IFrontier<ExplorationNode> _frontier;

        private readonly uint _maxObservedEvents;

        private ExplorationNode _violatingNode = null;

        private readonly SymbolFactory _symbolFactory = new();

        /// <summary>
        /// Constructs a new instance of the <see cref="MonitorAnalyzer"/> class."/>
        /// </summary>
        /// <param name="monitor">
        /// Typed AST of the monitor state machine to analyze.
        /// </param>
        /// /// <param name="fieldProvider">
        /// Object providing access to the runtime concrete values of monitor fields.
        /// </param>
        /// <param name="maxEvents">
        /// Maximum observed events in a single explored monitor execution.
        /// </param>
        /// <param name="strategy">
        /// Search strategy used to explore monitor executions.
        /// </param>
        internal MonitorAnalyzer(
            Machine monitor,
            IMonitorFieldProvider fieldProvider,
            uint maxEvents,
            SearchStrategy strategy)
        {
            _monitorAST = monitor;
            _fieldProvider = fieldProvider;
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
        /// Attempts to find a family of executions resulting in monitor assertion failures.
        /// </summary>
        /// <param name="currentStateName">
        /// Name of the current state of the runtime monitor state machine.
        /// </param>
        /// <param name="guidance">
        /// Monitor guidance representing a family of violating executions. 
        /// Set to null if false is returned.
        /// </param>
        /// <returns>True if violating executions were found, false otherwise.</returns>
        internal bool GetMonitorGuidance(
            string currentStateName,
            out MonitorGuidance guidance)
        {
            ResetAnalyzer();

            var currentState = _monitorAST.GetState(currentStateName)
                ?? throw new PInternalException(
                    $"State '{currentStateName}' was not found in monitor '{_monitorAST.Name}'.");

            var concreteFields = _fieldProvider.GetFieldValues();
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
                guidance = null;
                return false;
            }

            guidance = ComputeGuidance(_violatingNode);
            return true;
        }

        /// <summary>
        /// Returns hard-coded guidance for OneLeaderPerElection monitor
        /// </summary>
        /// <param name="currentStateName"></param>
        /// <param name="guidance"></param>
        /// <returns></returns>
        internal bool GetLeaderMonitorGuidance(
            string currentStateName,
            out MonitorGuidance guidance)
        {
            const string startStateName = "NoLeaderChosen";
            const string leaderChosenStateName = "LeaderChosen";
            const string chosenLeaderFieldName = "chosenLeader";
            const string leaderFieldName = "leader";

            guidance = null;

            if (_monitorAST.Observes?.Events == null)
            {
                throw new PInternalException(
                    $"Monitor '{_monitorAST.Name}' does not declare observed events.");
            }

            var observedEvents = _monitorAST.Observes.Events.ToList();
            if (observedEvents.Count != 1 || observedEvents[0].Name != "eLeaderChosen")
            {
                throw new PInternalException(
                    $"Fake leader-election guidance requires the monitor to observe only eLeaderChosen; " +
                    $"monitor '{_monitorAST.Name}' observes: {string.Join(", ", observedEvents.Select(e => e.Name))}.");
            }

            var eLeaderChosen = observedEvents[0];
            if (eLeaderChosen.PayloadType.Canonicalize() is not NamedTupleType payloadType ||
                !payloadType.LookupEntry(leaderFieldName, out var leaderEntry) ||
                !leaderEntry.Type.IsSameTypeAs(PrimitiveType.Int))
            {
                throw new PInternalException(
                    "Fake leader-election guidance requires eLeaderChosen to have an integer 'leader' payload field.");
            }

            var startState = _monitorAST.GetState(startStateName);
            var leaderChosenState = _monitorAST.GetState(leaderChosenStateName);
            if (startState == null || leaderChosenState == null)
            {
                throw new PInternalException(
                    $"Fake leader-election guidance requires states '{startStateName}' and '{leaderChosenStateName}'.");
            }

            var chosenLeaderField = _monitorAST.Fields.SingleOrDefault(
                field => field.Name == chosenLeaderFieldName);
            if (chosenLeaderField == null ||
                !chosenLeaderField.Type.IsSameTypeAs(PrimitiveType.Int))
            {
                throw new PInternalException(
                    $"Fake leader-election guidance requires integer monitor field '{chosenLeaderFieldName}'.");
            }

            if (!startState.AllEventHandlers.Any(handler => handler.Key.Name == eLeaderChosen.Name) ||
                !leaderChosenState.AllEventHandlers.Any(handler => handler.Key.Name == eLeaderChosen.Name))
            {
                throw new PInternalException(
                    "Fake leader-election guidance requires eLeaderChosen handlers in both monitor states.");
            }

            SymEvent CreateELeaderChosen() => new(
                eLeaderChosen,
                _symbolFactory.CreateFresh(eLeaderChosen.PayloadType));

            SymExpr GetLeader(SymEvent e)
            {
                if (e.Payload is not NamedTupleExpr payload ||
                    !payload.Fields.TryGetValue(leaderFieldName, out var leader))
                {
                    throw new PInternalException(
                        "Could not retrieve the 'leader' field from the symbolic eLeaderChosen payload.");
                }

                return leader;
            }

            List<(SymEvent e, State state)> violatingExecution = new();
            SymExpr violatingCondition;

            if (currentStateName == startStateName)
            {
                SymEvent first = CreateELeaderChosen();
                SymEvent second = CreateELeaderChosen();
                violatingExecution.Add((first, startState));
                violatingExecution.Add((second, leaderChosenState));

                violatingCondition = SymExprFactory.Not(SymExprFactory.Equal(
                    GetLeader(first), GetLeader(second)));
            }
            else if (currentStateName == leaderChosenStateName)
            {
                SymEvent second = CreateELeaderChosen();
                violatingExecution.Add((second, leaderChosenState));

                var fields = _fieldProvider.GetFieldValues();
                if (!fields.TryGetValue(chosenLeaderFieldName, out var chosenLeaderValue) ||
                    chosenLeaderValue == null)
                {
                    throw new PInternalException(
                        $"Missing concrete value for monitor field '{chosenLeaderFieldName}'.");
                }

                var chosenLeaderExpr = new ConcreteExpr(chosenLeaderValue, chosenLeaderField.Type);
                violatingCondition = SymExprFactory.Not(SymExprFactory.Equal(
                    chosenLeaderExpr, GetLeader(second)));
            }
            else
            {
                throw new PInternalException(
                    $"Unexpected current state '{currentStateName}' for fake leader-election guidance. " +
                    $"Expected '{startStateName}' or '{leaderChosenStateName}'.");
            }

            guidance = new(violatingExecution, violatingCondition);
            return true;
        }

        /// <summary>
        /// Returns hard-coded guidance for Mutex monitor
        /// </summary>
        internal bool GetMutexMonitorGuidance(
            string currentStateName,
            out MonitorGuidance guidance)
        {
            const string waitForHoldStateName = "WaitForHold";
            const string waitForReleaseStateName = "WaitForRelease";
            const string heldByFieldName = "heldBy";
            const string nodeFieldName = "node";

            guidance = null;

            if (_monitorAST.Observes?.Events == null)
            {
                throw new PInternalException(
                    $"Monitor '{_monitorAST.Name}' does not declare observed events.");
            }

            var observedEvents = _monitorAST.Observes.Events.ToList();
            var eHold = observedEvents.SingleOrDefault(e => e.Name == "eHold");
            var eRelease = observedEvents.SingleOrDefault(e => e.Name == "eRelease");
            if (observedEvents.Count != 2 || eHold == null || eRelease == null)
            {
                throw new PInternalException(
                    $"Fake mutex guidance requires the monitor to observe only eHold and eRelease; " +
                    $"monitor '{_monitorAST.Name}' observes: {string.Join(", ", observedEvents.Select(e => e.Name))}.");
            }

            bool HasIntegerNodePayload(ASTEvent eventType) =>
                eventType.PayloadType.Canonicalize() is NamedTupleType payloadType &&
                payloadType.LookupEntry(nodeFieldName, out var nodeEntry) &&
                nodeEntry.Type.IsSameTypeAs(PrimitiveType.Int);

            if (!HasIntegerNodePayload(eHold) || !HasIntegerNodePayload(eRelease))
            {
                throw new PInternalException(
                    "Fake mutex guidance requires eHold and eRelease to have an integer 'node' payload field.");
            }

            var waitForHoldState = _monitorAST.GetState(waitForHoldStateName);
            var waitForReleaseState = _monitorAST.GetState(waitForReleaseStateName);
            if (waitForHoldState == null || waitForReleaseState == null)
            {
                throw new PInternalException(
                    $"Fake mutex guidance requires states '{waitForHoldStateName}' and '{waitForReleaseStateName}'.");
            }

            var heldByField = _monitorAST.Fields.SingleOrDefault(
                field => field.Name == heldByFieldName);
            if (heldByField == null || !heldByField.Type.IsSameTypeAs(PrimitiveType.Int))
            {
                throw new PInternalException(
                    $"Fake mutex guidance requires integer monitor field '{heldByFieldName}'.");
            }

            if (!waitForHoldState.AllEventHandlers.Any(handler => handler.Key.Name == eHold.Name) ||
                !waitForReleaseState.AllEventHandlers.Any(handler => handler.Key.Name == eHold.Name))
            {
                throw new PInternalException(
                    "Fake mutex guidance requires eHold handlers in both monitor states.");
            }

            SymEvent CreateEHold() => new(
                eHold,
                _symbolFactory.CreateFresh(eHold.PayloadType));

            SymExpr GetNode(SymEvent e)
            {
                if (e.Payload is not NamedTupleExpr payload ||
                    !payload.Fields.TryGetValue(nodeFieldName, out var node))
                {
                    throw new PInternalException(
                        "Could not retrieve the 'node' field from the symbolic mutex event payload.");
                }

                return node;
            }

            List<(SymEvent e, State state)> violatingExecution = new();
            SymExpr violatingCondition;

            if (currentStateName == waitForHoldStateName)
            {
                SymEvent first = CreateEHold();
                SymEvent second = CreateEHold();
                violatingExecution.Add((first, waitForHoldState));
                violatingExecution.Add((second, waitForReleaseState));

                violatingCondition = SymExprFactory.Not(SymExprFactory.Equal(
                    GetNode(first), GetNode(second)));
            }
            else if (currentStateName == waitForReleaseStateName)
            {
                SymEvent second = CreateEHold();
                violatingExecution.Add((second, waitForReleaseState));

                var fields = _fieldProvider.GetFieldValues();
                if (!fields.TryGetValue(heldByFieldName, out var heldByValue) || heldByValue == null)
                {
                    throw new PInternalException(
                        $"Missing concrete value for monitor field '{heldByFieldName}'.");
                }

                var heldByExpr = new ConcreteExpr(heldByValue, heldByField.Type);
                violatingCondition = SymExprFactory.Not(SymExprFactory.Equal(
                    heldByExpr, GetNode(second)));
            }
            else
            {
                throw new PInternalException(
                    $"Unexpected current state '{currentStateName}' for fake mutex guidance. " +
                    $"Expected '{waitForHoldStateName}' or '{waitForReleaseStateName}'.");
            }

            guidance = new(violatingExecution, violatingCondition);
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
                SymExprFactory.True,
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
                    ASTEvent eventType = handler.Key;
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
        /// Computes monitor guidance leading to the given node.
        /// </summary>
        private MonitorGuidance ComputeGuidance(ExplorationNode node)
        {
            List<(SymEvent, State)> execution = new();
            var violationCondition = node.SymbolicState.PathCondition;

            while (node != null)
            {
                if (node.TransitionEvent != null)
                {
                    execution.Add((node.TransitionEvent, 
                        node.SymbolicState.CurrentState));
                }
                node = node.Parent;
            }

            execution.Reverse();
            // JR TODO: If path condition contains constraints over local variables,
            // or monitor fields, they must be removed; violation condition should
            // only contain constraints over symbolic event payloads
            return new MonitorGuidance(execution, violationCondition);
        }

        /// <summary>
        /// Returns whether the given event is observed by the monitor.
        /// </summary>
        internal bool IsEventObserved(RuntimeEvent e)
        {
            return _monitorAST.Observes.Events.Any(
                ev => MonitorGuidedStrategy.EventTypeMatches(ev, e));
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
