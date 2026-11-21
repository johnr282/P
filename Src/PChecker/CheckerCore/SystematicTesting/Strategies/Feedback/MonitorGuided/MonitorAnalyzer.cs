using PChecker.Runtime.Events;
using PChecker.Runtime.Exceptions;
using PChecker.Runtime.Values;
using PChecker.SystematicTesting.Strategies.Feedback.MonitorGuided.SymbolicExecution;
using Plang.Compiler.TypeChecker.AST.Declarations;
using Plang.Compiler.TypeChecker.AST.States;
using Plang.Compiler.TypeChecker.Types;
using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Guides AtomicityInvariant toward a successful write response for a transaction with no prepare replies.
        /// </summary>
        internal bool GetAtomicityMonitorGuidance(out MonitorGuidance guidance)
        {
            var observed = _monitorAST.Observes?.Events.ToArray();
            var initialize = observed?.SingleOrDefault(e => e.Name == "eMonitor_AtomicityInitialize");
            var prepare = observed?.SingleOrDefault(e => e.Name == "ePrepareResp");
            var acknowledge = observed?.SingleOrDefault(e => e.Name == "eDecisionAck");
            var write = observed?.SingleOrDefault(e => e.Name == "eWriteTransResp");
            var startState = _monitorAST.StartState;
            var waitState = _monitorAST.GetState("WaitForEvents");
            if (initialize?.PayloadType?.IsSameTypeAs(PrimitiveType.Int) != true || prepare == null ||
                acknowledge == null || write == null || startState?.Name != "Init" || waitState == null ||
                !HandlesEvent(startState, initialize) || !HandlesEvent(waitState, prepare) ||
                !HandlesEvent(waitState, acknowledge) || !HandlesEvent(waitState, write))
            {
                throw new PInternalException("Atomicity guidance requires integer initialization and handlers for " +
                    "prepare replies, decision acknowledgements, and write responses in WaitForEvents.");
            }

            RequireIntegerPayloadField(prepare, "transId");
            RequireIntegerPayloadField(acknowledge, "transId");
            RequireIntegerPayloadField(write, "transId");

            const int participantCount = 3;
            var symbols = new SymbolFactory();
            var initializePayload = symbols.CreateFresh(PrimitiveType.Int);
            var execution = new List<(SymEvent e, State state)>
            {
                (new SymEvent(initialize, initializePayload), startState)
            };
            SymExpr condition = SymExprFactory.Equal(initializePayload,
                new ConcreteExpr((PInt)participantCount, PrimitiveType.Int));

            NamedTupleExpr AddEvent(ASTEvent eventType)
            {
                var payload = (NamedTupleExpr)symbols.CreateFresh(eventType.PayloadType.Canonicalize());
                execution.Add((new SymEvent(eventType, payload), waitState));
                return payload;
            }

            void Require(SymExpr constraint)
            {
                condition = SymExprFactory.And(condition, constraint);
            }

            var committed = (NamedTupleExpr)symbols.CreateFresh(write.PayloadType.Canonicalize());
            var committedTransactionId = committed.Fields["transId"];
            void AddParticipantReplies(ASTEvent eventType, string resultField, string resultValue)
            {
                var expectedResult = RequireEnumPayloadValue(eventType, resultField, resultValue);
                var participants = new List<SymExpr>(participantCount);
                for (var i = 0; i < participantCount; i++)
                {
                    var reply = AddEvent(eventType);
                    Require(SymExprFactory.Equal(reply.Fields["transId"], committedTransactionId));
                    Require(SymExprFactory.Equal(reply.Fields[resultField], expectedResult));

                    var participant = reply.Fields["participant"];
                    foreach (var previous in participants)
                    {
                        Require(SymExprFactory.Not(SymExprFactory.Equal(participant, previous)));
                    }
                    participants.Add(participant);
                }
            }

            AddParticipantReplies(prepare, "status", "SUCCESS");
            AddParticipantReplies(acknowledge, "decision", "COMMIT");

            var success = RequireEnumPayloadValue(write, "status", "SUCCESS");
            execution.Add((new SymEvent(write, committed), waitState));
            Require(SymExprFactory.Equal(committed.Fields["status"], success));

            // A successful write can precede the benchmark's spurious success for another transaction.
            var unprepared = AddEvent(write);
            Require(SymExprFactory.Not(SymExprFactory.Equal(unprepared.Fields["transId"], committedTransactionId)));
            Require(SymExprFactory.Equal(unprepared.Fields["status"], success));

            guidance = new MonitorGuidance(execution, condition);
            return true;
        }

        /// <summary>
        /// Guides OneValueTaught from the first learned value to a conflicting value.
        /// </summary>
        internal bool GetOneValueTaughtMonitorGuidance(out MonitorGuidance guidance)
        {
            var observed = _monitorAST.Observes?.Events.ToArray();
            if (observed == null || observed.Length != 1 || observed[0].Name != "eLearn")
            {
                throw new PInternalException("OneValueTaught guidance requires the monitor to observe only eLearn.");
            }

            guidance = CreateTwoEventGuidance(observed[0], "v", "decided", "Init", "Init",
                uninitializedValue: -1);
            return true;
        }

        private MonitorGuidance CreateTwoEventGuidance(ASTEvent eventType, string payloadField, string monitorField,
            string startStateName, string nextStateName, long? uninitializedValue = null)
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
            var firstValue = firstPayload.Fields[payloadField];
            var secondValue = secondPayload.Fields[payloadField];
            var condition = SymExprFactory.Not(SymExprFactory.Equal(firstValue, secondValue));
            if (uninitializedValue is long sentinel)
            {
                var unset = new ConcreteExpr((PInt)sentinel, PrimitiveType.Int);
                condition = SymExprFactory.And(condition, SymExprFactory.Not(SymExprFactory.Equal(firstValue, unset)));
                condition = SymExprFactory.And(condition, SymExprFactory.Not(SymExprFactory.Equal(secondValue, unset)));
            }

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

        private static ConcreteExpr RequireEnumPayloadValue(ASTEvent eventType, string fieldName, string valueName)
        {
            if (eventType.PayloadType?.Canonicalize() is NamedTupleType payloadType &&
                payloadType.LookupEntry(fieldName, out var field) &&
                field.Type.Canonicalize() is EnumType enumType &&
                enumType.EnumDecl.Values.SingleOrDefault(value => value.Name == valueName) is { } element)
            {
                return new ConcreteExpr((PInt)element.Value, enumType);
            }

            throw new PInternalException(
                $"Guidance requires event '{eventType.Name}' to have enum value '{valueName}' for '{fieldName}'.");
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
