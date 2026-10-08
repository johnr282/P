using PChecker.Runtime.Exceptions;
using PChecker.SystematicTesting.Operations;
using PChecker.Runtime.Events;
using PChecker.Runtime.Values;
using System;
using System.Collections.Generic;
using System.Linq;
using PChecker.Runtime.StateMachines;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    /// <summary>
    /// Stores concrete observed behaviors for a single state machine in a trie
    /// data structure. 
    /// </summary>
    internal class BehaviorStore
    {
        internal sealed class BehaviorNode
        {
            public Dictionary<SchedulingChoice, BehaviorNode> Transitions { get; } 
                = new(BehaviorStoreComparers.ChoiceEquality);
            public IReadOnlyList<ExecutionEffect> Effects { get; }

            /// <summary>
            /// True if this node represents a complete, uninterrupted behavior. 
            /// False otherwise.
            /// </summary>
            public bool CompleteBehavior { get; }

            public BehaviorNode(
                IReadOnlyList<ExecutionEffect> effects, 
                bool completeBehavior)
            {
                Effects = effects;
                CompleteBehavior = completeBehavior;
            }

            /// <summary>
            /// Adds a new behavior to this node if the new behavior is consistent
            /// with existing behavior. 
            /// </summary>
            /// <returns>
            /// False if a transition already exists for the given choice but the 
            /// effects are inconsistent with the given effects, true otherwise.
            /// </returns>
            public bool AddBehavior(
                SchedulingChoice choice,
                IReadOnlyList<ExecutionEffect> effects, 
                bool completeBehavior)
            {
                if (!Transitions.TryGetValue(choice, out var nextNode))
                {
                    Transitions[choice] = new BehaviorNode(effects, completeBehavior);
                    return true;
                }

                if (nextNode.CompleteBehavior)
                {
                    if (completeBehavior)
                    {
                        // Existing and new behaviors are both complete, so they 
                        // should match exactly
                        return nextNode.Effects.SequenceEqual(effects,
                            BehaviorStoreComparers.EffectEquality);
                    }

                    // New incomplete behavior should be a prefix of existing
                    // complete behavior
                    return PrefixOf(effects, nextNode.Effects);
                }

                // Existing behavior is incomplete; replace it only if new behavior
                // is more complete
                if (completeBehavior)
                {
                    // Replace existing incomplete behavior with new complete 
                    // behavior; existing should be a prefix of new
                    if (PrefixOf(nextNode.Effects, effects))
                    {
                        Transitions[choice] = new BehaviorNode(effects, true);
                        return true;
                    }
                    return false;
                }

                // Both are incomplete; shorter behavior should be a prefix of
                // the longer. Store the longer behavior if they are consistent.
                IReadOnlyList<ExecutionEffect> shorter, longer;
                if (nextNode.Effects.Count >= effects.Count)
                {
                    shorter = effects;
                    longer = nextNode.Effects;
                }
                else
                {
                    shorter = nextNode.Effects;
                    longer = effects;
                }

                if (PrefixOf(shorter, longer))
                {
                    Transitions[choice] = new BehaviorNode(longer, false);
                    return true;
                }
                return false;
            }

            /// <summary>
            /// Returns whether prefix is a prefix of effects.
            /// </summary>
            private static bool PrefixOf(
                IReadOnlyList<ExecutionEffect> prefix, 
                IReadOnlyList<ExecutionEffect> effects)
            {
                return effects.Count >= prefix.Count &&
                    effects.Take(prefix.Count).SequenceEqual(
                        prefix,
                        BehaviorStoreComparers.EffectEquality);
            }
        }

        private BehaviorNode _root = new(new List<ExecutionEffect>(), true);

        /// <summary>
        /// Adds the given behavior to the store. The behavior represents the 
        /// following: Suppose this store corresponds to state machine i. When 
        /// machine i with previous choices trace executes newChoice, it produces 
        /// effects.
        /// </summary>
        /// <param name="trace">Local trace of machine i.</param>
        /// <param name="newChoice">Most recent choice executed on machine i.</param>
        /// <param name="effects">Effects produced after execution of newChoice.</param>
        /// <param name="completeBehavior">
        /// True if this is a complete behavior, meaning machine i finished its
        /// scheduled execution without interruption. False otherwise.
        /// </param>
        /// <exception cref="PInternalException"></exception>
        public void AddBehavior(
            IReadOnlyList<SchedulingChoice> trace,
            SchedulingChoice newChoice,
            IReadOnlyList<ExecutionEffect> effects, 
            bool completeBehavior)
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

            

            if (!current.AddBehavior(newChoice, effects, completeBehavior))
            {
                throw new PInternalException(
                    $"Inconsistent behavior for choice {newChoice} in trace " +
                    $"{string.Join(", ", trace)}. Recorded: {string.Join(", ", current.Effects)}" +
                    $", observed: {string.Join(", ", effects)}");
            }
        }

        /// <summary>
        /// Attempts to find a recorded behavior corresponding to the given trace
        /// and next choice.
        /// </summary>
        /// <returns>True if behavior was found, false if not.</returns>
        public bool GetBehavior(
            IReadOnlyList<SchedulingChoice> trace, 
            SchedulingChoice nextChoice, 
            out IReadOnlyList<ExecutionEffect> effects, 
            out bool completeBehavior)
        {
            effects = null;
            completeBehavior = false;
            var current = _root;
            var nextTrace = trace.Append(nextChoice);

            foreach (var choice in nextTrace)
            {
                if (!current.Transitions.TryGetValue(choice, out var nextNode))
                {
                    return false;
                }
                current = nextNode;
            }

            effects = current.Effects;
            completeBehavior = current.CompleteBehavior;
            return true;
        }
    }

    internal class BehaviorStoreComparers
    {
        internal static readonly ChoiceComparer ChoiceEquality = new();
        internal static readonly EffectComparer EffectEquality = new();
        private static readonly PayloadComparer PayloadEquality = new();

        private sealed class PayloadComparer : IEqualityComparer<IPValue>
        {
            public bool Equals(IPValue x, IPValue y) => SamePValue(x, y);
            public int GetHashCode(IPValue value) => PValueHash(value);
        }

        // Compare machine references by creation path. Collection enumeration order
        // is part of behavior identity: handlers can observe it through iteration
        // and map keys/values even when ordinary P value equality ignores it.
        private static bool SamePValue(IPValue x, IPValue y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            return (x, y) switch
            {
                (PFloat a, PFloat b) => FloatBits(a) == FloatBits(b),
                (PMachineValue a, PMachineValue b) => SameStateMachineId(a.Id, b.Id),
                (PTuple a, PTuple b) =>
                    a.fieldValues.SequenceEqual(b.fieldValues, PayloadEquality),
                (PNamedTuple a, PNamedTuple b) =>
                    a.fieldNames.SequenceEqual(b.fieldNames, StringComparer.Ordinal) &&
                    a.fieldValues.SequenceEqual(b.fieldValues, PayloadEquality),
                (PSeq a, PSeq b) => a.SequenceEqual(b, PayloadEquality),
                (PSet a, PSet b) => a.SequenceEqual(b, PayloadEquality),
                (PMap a, PMap b) => SameMap(a, b),
                (Event a, Event b) => SameEvent(a, b),
                // Other primitive and foreign values retain their own value semantics.
                _ => x.Equals(y)
            };
        }

        // Behavior identity uses the exact floating-point representation: +0 and
        // -0 have different reciprocals, while identical NaN representations must
        // compare equal across observations. Hash the same bits for consistency.
        private static long FloatBits(PFloat value) => BitConverter.DoubleToInt64Bits((double)value);

        private static bool SameMap(PMap x, PMap y)
        {
            if (x.Count != y.Count) return false;
            return x.Zip(y, (a, b) =>
                SamePValue(a.Key, b.Key) && SamePValue(a.Value, b.Value)).All(equal => equal);
        }

        private static int PValueHash(IPValue value) => value switch
        {
            null => 0,
            PFloat number => FloatBits(number).GetHashCode(),
            PMachineValue machine => StateMachineIdHash(machine.Id),
            PTuple tuple => OrderedHash(tuple.fieldValues),
            PNamedTuple tuple => HashCode.Combine(
                OrderedHash(tuple.fieldValues), NamedFieldsHash(tuple.fieldNames)),
            PSeq sequence => OrderedHash(sequence),
            PSet set => OrderedHash(set),
            PMap map => MapHash(map),
            Event e => EventHash(e),
            _ => value.GetHashCode()
        };

        private static int OrderedHash(IEnumerable<IPValue> values)
        {
            var hash = new HashCode();
            foreach (var value in values) hash.Add(PValueHash(value));
            return hash.ToHashCode();
        }

        private static int NamedFieldsHash(IEnumerable<string> names)
        {
            var hash = new HashCode();
            foreach (var name in names) hash.Add(name, StringComparer.Ordinal);
            return hash.ToHashCode();
        }

        private static int MapHash(PMap map)
        {
            var hash = new HashCode();
            foreach (var entry in map)
            {
                hash.Add(PValueHash(entry.Key));
                hash.Add(PValueHash(entry.Value));
            }
            return hash.ToHashCode();
        }

        // Compare events using both type and payload rather than Event.Equals,
        // which ignores payloads.
        public static bool SameEvent(Event x, Event y) =>
            ReferenceEquals(x, y) || 
            x != null && y != null &&
            x.GetType() == y.GetType() && 
            SamePValue(x.Payload, y.Payload);

        private static int EventHash(Event e) =>
            e is null ? 0 : HashCode.Combine(e.GetType(), PValueHash(e.Payload));

        // Purposefully ignore EventInfo; cannot influence execution. 
        private static bool SameEventWithMetadata((Event e, EventInfo info) x,
            (Event e, EventInfo info) y) =>
            SameEvent(x.e, y.e);

        private static int EventWithMetadataHash((Event e, EventInfo info) input) =>    
            EventHash(input.e);

        public static bool SameStateMachineId(StateMachineId x, StateMachineId y) =>
            ReferenceEquals(x, y) || 
            x != null && y != null &&
            object.Equals(x.CreationPath, y.CreationPath) &&
            StringComparer.Ordinal.Equals(x.Type, y.Type) &&
            StringComparer.Ordinal.Equals(x.InterfaceName, y.InterfaceName);

        private static int StateMachineIdHash(StateMachineId id) =>
            id is null ? 0 : HashCode.Combine(id.CreationPath, id.Type, id.InterfaceName);

        /// <summary>
        /// Compares choices by concrete operation identity and input values.
        /// Payloads must not change while their choices are stored as trie keys.
        /// </summary>
        internal sealed class ChoiceComparer : IEqualityComparer<SchedulingChoice>
        {
            private sealed record ChoiceEvents(
                (Event e, EventInfo info) First, 
                (Event e, EventInfo info) Second);

            private static ChoiceEvents GetEventsFromChoice(SchedulingChoice choice) =>
                choice switch
                {
                    InitializeChoice c => 
                        new ChoiceEvents((c.InitialEvent, null), default),
                    ResumeInitializationChoice c => 
                        new ChoiceEvents((c.InitialEvent, null), default),
                    DeliverEventChoice c => 
                        new ChoiceEvents(c.EventToDeliver, default),
                    ResumeHandlerChoice c => 
                        new ChoiceEvents(c.EventToResume, default),
                    CompleteReceiveChoice c => new ChoiceEvents(
                        c.EventToResume,
                        c.EventToDeliver),

                    _ => throw new NotSupportedException(
                        $"Unsupported scheduling choice: {choice.GetType()}")
                };

            public bool Equals(SchedulingChoice x, SchedulingChoice y)
            {
                if (ReferenceEquals(x, y)) return true;

                if (x is null || y is null || 
                    x.GetType() != y.GetType())
                    return false;

                var xId = x.GetStateMachineId();
                var yId = y.GetStateMachineId();

                if (xId == null && yId == null)
                {
                    // Choices do not have associated state machines, so simply
                    // compare operations
                    return x.OperationId == y.OperationId;
                }

                var xEvents = GetEventsFromChoice(x);
                var yEvents = GetEventsFromChoice(y);
                return SameStateMachineId(xId, yId) &&
                    SameEventWithMetadata(xEvents.First, yEvents.First) &&
                    SameEventWithMetadata(xEvents.Second, yEvents.Second);
            }

            public int GetHashCode(SchedulingChoice choice)
            {
                if (choice is null) return 0;

                var hash = new HashCode();

                var choiceMachineId = choice.GetStateMachineId();
                if (choiceMachineId == null)
                {
                    return HashCode.Combine(choice.GetType(),
                        choice.OperationId);
                }
                else
                {
                    hash.Add(StateMachineIdHash(choiceMachineId));
                }

                var inputs = GetEventsFromChoice(choice);
                hash.Add(choice.GetType());
                hash.Add(EventWithMetadataHash(inputs.First));
                hash.Add(EventWithMetadataHash(inputs.Second));
                return hash.ToHashCode();
            }
        }

        internal sealed class EffectComparer : IEqualityComparer<ExecutionEffect>
        {
            public bool Equals(ExecutionEffect x, ExecutionEffect y)
            {
                if (ReferenceEquals(x, y)) return true;

                if (x is null || y is null || 
                    x.GetType() != y.GetType() ||
                    !SameStateMachineId(x.StateMachineId, y.StateMachineId))
                    return false;

                // Effects were produced by same machine, so compare effect details
                return (x, y) switch
                {
                    (SendEffect a, SendEffect b) =>
                        SameStateMachineId(a.TargetStateMachineId, 
                            b.TargetStateMachineId) &&
                        SameEventWithMetadata(a.SentEvent, b.SentEvent),

                    (MonitorObservationEffect a, MonitorObservationEffect b) =>
                        SameEvent(a.ObservedEvent, b.ObservedEvent),

                    (CreateEffect a, CreateEffect b) =>
                        SameStateMachineId(a.CreatedStateMachineId, 
                            b.CreatedStateMachineId) &&
                        SameEvent(a.InitialEvent, b.InitialEvent),

                    (BlockOnReceiveEffect a, BlockOnReceiveEffect b) =>
                        SameEvent(a.InProgressEvent.e, b.InProgressEvent.e),

                    (CompleteHandlerEffect a, CompleteHandlerEffect b) =>
                        SameEvent(a.EventOfCompletedHandler.e, 
                            b.EventOfCompletedHandler.e),

                    (HaltEffect a, HaltEffect b) => true,

                    _ => throw new NotSupportedException(
                        $"Unsupported execution effect: {x.GetType()}")
                };
            }

            public int GetHashCode(ExecutionEffect effect)
            {
                if (effect is null) return 0;

                var detail = effect switch
                {
                    SendEffect e => HashCode.Combine(
                        StateMachineIdHash(e.TargetStateMachineId), 
                        EventWithMetadataHash(e.SentEvent)),

                    MonitorObservationEffect e => EventHash(e.ObservedEvent),

                    CreateEffect e => HashCode.Combine(
                        StateMachineIdHash(e.CreatedStateMachineId),
                        EventHash(e.InitialEvent)),

                    _ => throw new NotSupportedException(
                        $"Unsupported execution effect: {effect.GetType()}")
                };
                return HashCode.Combine(
                    effect.GetType(), 
                    StateMachineIdHash(effect.StateMachineId), 
                    detail);
            }
        }
    }
}
