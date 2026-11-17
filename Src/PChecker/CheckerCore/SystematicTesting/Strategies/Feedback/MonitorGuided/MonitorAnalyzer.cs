using PChecker.Runtime.Events;
using PChecker.Runtime.Exceptions;
using PChecker.SystematicTesting.Strategies.MonitorGuided;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution;
using Plang.Compiler.TypeChecker.AST.Declarations;
using Plang.Compiler.TypeChecker.AST.States;
using Plang.Compiler.TypeChecker.Types;
using System;
using System.Linq;
using ASTEvent = Plang.Compiler.TypeChecker.AST.Declarations.Event;
using RuntimeEvent = PChecker.Runtime.Events.Event;

namespace PChecker.SystematicTesting.Strategies.Feedback.MonitorGuided
{
    internal class MonitorAnalyzer
    {
        private readonly Machine _monitorAST;

        internal MonitorAnalyzer(Machine monitorAST)
        {
            _monitorAST = monitorAST;
        }

        /// <summary>
        /// Guides OneLeaderPerElection from its start state to two different leaders.
        /// </summary>
        internal bool GetLeaderMonitorGuidance(out MonitorGuidance guidance)
        {
            var observed = _monitorAST.Observes?.Events.ToArray();
            if (observed == null || observed.Length != 1 || observed[0].Name != "eLeaderChosen")
            {
                throw new PInternalException("Leader-election guidance requires the monitor to observe only eLeaderChosen.");
            }

            guidance = CreateTwoEventGuidance(observed[0], "leader", "chosenLeader", "NoLeaderChosen", "LeaderChosen");
            return true;
        }

        /// <summary>
        /// Guides Mutex from its start state to holds by two different nodes with no intervening release.
        /// </summary>
        internal bool GetMutexMonitorGuidance(out MonitorGuidance guidance)
        {
            var observed = _monitorAST.Observes?.Events.ToArray();
            var hold = observed?.SingleOrDefault(e => e.Name == "eHold");
            var release = observed?.SingleOrDefault(e => e.Name == "eRelease");
            if (observed?.Length != 2 || hold == null || release == null)
            {
                throw new PInternalException("Mutex guidance requires the monitor to observe only eHold and eRelease.");
            }

            RequireIntegerPayloadField(release, "node");
            guidance = CreateTwoEventGuidance(hold, "node", "heldBy", "WaitForHold", "WaitForRelease");
            return true;
        }

        private MonitorGuidance CreateTwoEventGuidance(ASTEvent eventType, string payloadField, string monitorField,
            string startStateName, string nextStateName)
        {
            RequireIntegerPayloadField(eventType, payloadField);

            var startState = _monitorAST.StartState;
            var nextState = _monitorAST.GetState(nextStateName);
            if (startState?.Name != startStateName || nextState == null)
            {
                throw new PInternalException($"Guidance for '{_monitorAST.Name}' requires start state " +
                    $"'{startStateName}' and state '{nextStateName}'.");
            }

            if (!_monitorAST.LookupEntry(monitorField, out var field) || !field.Type.IsSameTypeAs(PrimitiveType.Int))
            {
                throw new PInternalException($"Guidance for '{_monitorAST.Name}' requires integer field '{monitorField}'.");
            }

            if (!HandlesEvent(startState, eventType) || !HandlesEvent(nextState, eventType))
            {
                throw new PInternalException($"Guidance for '{_monitorAST.Name}' requires '{eventType.Name}' handlers " +
                    $"in both '{startStateName}' and '{nextStateName}'.");
            }

            // The first handler overwrites the tracked field before the second handler reads it.
            var symbols = new SymbolFactory();
            var payloadType = eventType.PayloadType.Canonicalize();
            var firstPayload = (NamedTupleExpr)symbols.CreateFresh(payloadType);
            var secondPayload = (NamedTupleExpr)symbols.CreateFresh(payloadType);
            var condition = SymExprFactory.Not(SymExprFactory.Equal(
                firstPayload.Fields[payloadField], secondPayload.Fields[payloadField]));

            return new MonitorGuidance(new[]
            {
                (new SymEvent(eventType, firstPayload), startState),
                (new SymEvent(eventType, secondPayload), nextState)
            }, condition);
        }

        private static bool HandlesEvent(State state, ASTEvent eventType) =>
            state.AllEventHandlers.Any(handler => handler.Key == eventType &&
                handler.Value is EventDoAction or EventGotoState);

        private static void RequireIntegerPayloadField(ASTEvent eventType, string fieldName)
        {
            if (eventType.PayloadType?.Canonicalize() is not NamedTupleType payloadType ||
                !payloadType.LookupEntry(fieldName, out var entry) || !entry.Type.IsSameTypeAs(PrimitiveType.Int))
            {
                throw new PInternalException($"Guidance requires event '{eventType.Name}' to have an integer " +
                    $"'{fieldName}' payload field.");
            }
        }

        /// <summary>
        /// Returns whether the given event is handled by the given monitor at the 
        /// given state.
        /// </summary>
        internal static bool EventHandledByMonitor(RuntimeEvent e, Machine monitorAST, State monitorState)
        {
            if (e == null ||
                !IsEventObserved(e, monitorAST))
                return false;

            return monitorState.AllEventHandlers.Any(handler =>
                EventTypeMatches(handler.Key, e) &&
                handler.Value is not EventIgnore);
        }

        /// <summary>
        /// Returns whether the given event is observed by the monitor.
        /// </summary>
        internal static bool IsEventObserved(RuntimeEvent e, Machine monitorAST)
        {
            return monitorAST.Observes.Events.Any(ev => EventTypeMatches(ev, e));
        }

        internal static bool EventTypeMatches(ASTEvent expected, RuntimeEvent actual)
        {
            if (expected.IsHaltEvent)
                return actual is PHalt;

            if (expected.IsNullEvent)
                return actual is DefaultEvent;

            return string.Equals(expected.Name, actual.GetType().Name, StringComparison.Ordinal);
        }
    }
}
