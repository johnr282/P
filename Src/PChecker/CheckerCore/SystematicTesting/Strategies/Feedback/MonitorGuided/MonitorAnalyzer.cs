using System.Linq;
using PChecker.Runtime.Exceptions;
using PChecker.SystematicTesting.Strategies.MonitorGuided;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution;
using Plang.Compiler.TypeChecker.AST.Declarations;
using Plang.Compiler.TypeChecker.AST.States;
using Plang.Compiler.TypeChecker.Types;

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

        private MonitorGuidance CreateTwoEventGuidance(Event eventType, string payloadField, string monitorField,
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

        private static bool HandlesEvent(State state, Event eventType) =>
            state.AllEventHandlers.Any(handler => handler.Key == eventType &&
                handler.Value is EventDoAction or EventGotoState);

        private static void RequireIntegerPayloadField(Event eventType, string fieldName)
        {
            if (eventType.PayloadType?.Canonicalize() is not NamedTupleType payloadType ||
                !payloadType.LookupEntry(fieldName, out var entry) || !entry.Type.IsSameTypeAs(PrimitiveType.Int))
            {
                throw new PInternalException($"Guidance requires event '{eventType.Name}' to have an integer " +
                    $"'{fieldName}' payload field.");
            }
        }
    }
}
