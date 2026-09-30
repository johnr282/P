using PChecker.Runtime.Exceptions;
using PChecker.SystematicTesting.Operations;
using PChecker.Runtime.Events;
using PChecker.Runtime.Values;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    /// <summary>
    /// Stores concrete observed behaviors for a single state machine in a trie
    /// data structure. 
    /// </summary>
    internal class BehaviorStore
    {
        private static readonly ChoiceComparer ChoiceEquality = new();
        private static readonly EffectComparer EffectEquality = new();

        // Compare payloads using P value semantics rather than Event.Equals,
        // which ignores payloads.
        private static bool SameEvent(Event x, Event y) =>
            ReferenceEquals(x, y) || x != null && y != null &&
            x.GetType() == y.GetType() && PValues.SafeEquals(x.Payload, y.Payload);

        private static int EventHash(Event e) =>
            e is null ? 0 : HashCode.Combine(e.GetType(), e.Payload);

        private static bool SameEventWithMetadata((Event e, EventInfo info) x, 
            (Event e, EventInfo info) y) =>
            SameEvent(x.e, y.e) && object.Equals(
                x.info?.OriginInfo?.SenderStateMachineId,
                y.info?.OriginInfo?.SenderStateMachineId);

        private static int EventWithMetadataHash((Event e, EventInfo info) input) =>
            HashCode.Combine(
                EventHash(input.e), 
                input.info?.OriginInfo?.SenderStateMachineId);

        /// <summary>
        /// Compares choices by concrete operation identity and input values.
        /// Payloads must not change while their choices are stored as trie keys.
        /// </summary>
        private sealed class ChoiceComparer : IEqualityComparer<SchedulingChoice>
        {
            private static ((Event e, EventInfo info) First, (Event e, EventInfo info) Second, bool InInitialization)
                Inputs(SchedulingChoice choice) =>
                choice switch
                {
                    InitializeChoice c => ((c.InitialEvent, null), default, false),
                    ResumeInitializationChoice c => ((c.InitialEvent, null), default, false),
                    DeliverEventChoice c => (c.EventToDeliver, default, false),
                    ResumeHandlerChoice c => (c.EventToResume, default, false),
                    CompleteReceiveChoice c => (
                        c.EventToResume,
                        c.EventToDeliver,
                        c.InInitialization),
                    RunTaskChoice => (default, default, false),

                    _ => throw new NotSupportedException(
                        $"Unsupported scheduling choice: {choice.GetType()}")
                };

            public bool Equals(SchedulingChoice x, SchedulingChoice y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x is null || y is null || x.GetType() != y.GetType() ||
                    x.Operation.Id != y.Operation.Id ||
                    !StringComparer.Ordinal.Equals(x.Operation.Name, y.Operation.Name)) 
                    return false;

                var a = Inputs(x);
                var b = Inputs(y);
                return a.InInitialization == b.InInitialization &&
                    SameEventWithMetadata(a.First, b.First) && SameEventWithMetadata(a.Second, b.Second);
            }

            public int GetHashCode(SchedulingChoice choice)
            {
                if (choice is null) return 0;
                var inputs = Inputs(choice);
                return HashCode.Combine(choice.GetType(), choice.Operation.Id, 
                    choice.Operation.Name, EventWithMetadataHash(inputs.First),
                    EventWithMetadataHash(inputs.Second), inputs.InInitialization);
            }
        }

        private sealed class EffectComparer : IEqualityComparer<ExecutionEffect>
        {
            public bool Equals(ExecutionEffect x, ExecutionEffect y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x is null || y is null || x.GetType() != y.GetType() ||
                    !object.Equals(x.StateMachineId, y.StateMachineId)) 
                    return false;

                return (x, y) switch
                {
                    (SendEffect a, SendEffect b) =>
                        object.Equals(a.TargetStateMachineId, b.TargetStateMachineId) &&
                        SameEvent(a.SentEvent, b.SentEvent),
                    (AnnounceEffect a, AnnounceEffect b) => 
                        SameEvent(a.AnnouncedEvent, b.AnnouncedEvent),
                    (CreateEffect a, CreateEffect b) =>
                        a.CreatedStateMachineType == b.CreatedStateMachineType &&
                        StringComparer.Ordinal.Equals(a.CreatedStateMachineName, b.CreatedStateMachineName) &&
                        SameEvent(a.InitialEvent, b.InitialEvent),

                    _ => throw new NotSupportedException(
                        $"Unsupported execution effect: {x.GetType()}")
                };
            }

            public int GetHashCode(ExecutionEffect effect)
            {
                if (effect is null) return 0;
                var detail = effect switch
                {
                    SendEffect e => 
                        HashCode.Combine(e.TargetStateMachineId, EventHash(e.SentEvent)),
                    AnnounceEffect e => EventHash(e.AnnouncedEvent),
                    CreateEffect e => HashCode.Combine(
                        e.CreatedStateMachineType,
                        e.CreatedStateMachineName, 
                        EventHash(e.InitialEvent)),

                    _ => throw new NotSupportedException(
                        $"Unsupported execution effect: {effect.GetType()}")
                };
                return HashCode.Combine(effect.GetType(), effect.StateMachineId, detail);
            }
        }

        internal sealed class BehaviorNode
        {
            public Dictionary<SchedulingChoice, BehaviorNode> Transitions { get; } 
                = new(ChoiceEquality);
            public IReadOnlyList<ExecutionEffect> Effects { get; }

            public BehaviorNode(IReadOnlyList<ExecutionEffect> effects)
            {
                Effects = effects;
            }

            /// <summary>
            /// Adds a new behavior to this node. 
            /// </summary>
            /// <returns>
            /// False if a transition already exists for the given choice but the 
            /// effects are inconsistent with the given effects, true otherwise.
            /// </returns>
            public bool AddBehavior(
                SchedulingChoice choice,
                IReadOnlyList<ExecutionEffect> effects)
            {
                if (!Transitions.TryGetValue(choice, out var nextNode))
                {
                    Transitions[choice] = new BehaviorNode(effects);
                    return true;
                }

                return nextNode.Effects.SequenceEqual(effects, EffectEquality);
            }
        }

        private BehaviorNode _root = new(new List<ExecutionEffect>());

        /// <summary>
        /// Adds the given behavior to the store. The behavior represents the 
        /// following: Suppose this store corresponds to state machine i. When 
        /// machine i with previous choices trace executes newChoice, it produces 
        /// effects.
        /// </summary>
        public void AddBehavior(
            IReadOnlyList<SchedulingChoice> trace,
            SchedulingChoice newChoice,
            IReadOnlyList<ExecutionEffect> effects)
        {
            var current = _root;

            // Behaviors should be added for every new choice, so a node
            // corresponding to trace should already exist.
            foreach (var choice in trace)
            {
                if (!current.Transitions.TryGetValue(choice, out var nextNode))
                {
                    throw new PInternalException(
                        $"Expected a behavior node for choice {choice} in trace " +
                        $"{string.Join(", ", trace)} but none was found.");
                }

                current = nextNode;
            }

            if (!current.AddBehavior(newChoice, effects))
            {
                throw new PInternalException(
                    $"Inconsistent behavior for choice {newChoice} in trace " +
                    $"{string.Join(", ", trace)}. Recorded: {string.Join(", ", current.Effects)}" +
                    $", observed: {string.Join(", ", effects)}");
            }
        }

        /// <summary>
        /// Attempts to find a recorded behavior corresponding to the given trace.
        /// </summary>
        /// <returns>True if behavior was found, false if not.</returns>
        public bool GetBehavior(
            List<SchedulingChoice> trace, 
            out IReadOnlyList<ExecutionEffect> effects)
        {
            effects = new List<ExecutionEffect>();
            var current = _root;

            foreach (var choice in trace)
            {
                if (!current.Transitions.TryGetValue(choice, out var nextNode))
                {
                    return false;
                }
                current = nextNode;
            }

            effects = current.Effects;
            return true;
        }
    }
}
