using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using PChecker.Configuration;
using PChecker.Random;
using PChecker.Runtime.Events;
using PChecker.Runtime.Exceptions;
using PChecker.Runtime.StateMachines;
using PChecker.Runtime.Values;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Strategies.MonitorGuided;
using PChecker.SystematicTesting.Strategies.Probabilistic;
using ControlledRuntime = PChecker.SystematicTesting.ControlledRuntime;

namespace UnitTests;

[TestFixture]
public class BehaviorStoreTests
{
    private static readonly ConditionalWeakTable<ControlledRuntime, MachineCreationPathFactory>
        PathFactories = new();
    private static ControlledRuntime NewRuntime()
    {
        var config = CheckerConfiguration.Create();
        return new ControlledRuntime(config, new RandomStrategy(10, new RandomValueGenerator(config)));
    }

    private static StateMachineOperation Operation(ControlledRuntime runtime, string name = "Machine",
        MachineCreationPath path = null, Type type = null)
    {
        var machine = new TestMachine();
        path ??= PathFactories.GetOrCreateValue(runtime).NewPath(null);
        machine.Configure(runtime, new StateMachineId(type ?? typeof(TestMachine), name, path, runtime), null, null, null);
        return new StateMachineOperation(machine);
    }

    private static SchedulingChoice Choice(int kind, StateMachineOperation op, int payload = 1,
        int receivedPayload = 2, bool initializing = false)
    {
        var e = new Event(new PInt(payload));
        return kind switch
        {
            0 => new InitializeChoice(op.Id, op.StateMachine.Id, e),
            1 => new ResumeInitializationChoice(op.Id, op.StateMachine.Id, e),
            2 => new DeliverEventChoice(op.Id, op.StateMachine.Id, (e, new EventInfo(e))),
            3 => new ResumeHandlerChoice(op.Id, op.StateMachine.Id, (e, new EventInfo(e))),
            4 => new CompleteReceiveChoice(op.Id, op.StateMachine.Id, (e, null),
                (new Event(new PInt(receivedPayload)), null), initializing),
            _ => new RunTaskChoice(op.Id)
        };
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void EquivalentChoicesFromSeparateOperationsRetrieveRecordedBehavior(int kind)
    {
        using var firstRuntime = NewRuntime();
        using var secondRuntime = NewRuntime();
        var first = Choice(kind, Operation(firstRuntime));
        var second = Choice(kind, Operation(secondRuntime));
        var store = new BehaviorStore();
        var effects = new ExecutionEffect[] { new AnnounceEffect(null, new Event(new PInt(7))) };
        store.AddBehavior(Array.Empty<SchedulingChoice>(), first, effects, true);

        Assert.That(store.GetBehavior(new List<SchedulingChoice> { second }, out var result, out var complete), Is.True);
        Assert.That(complete, Is.True);
        Assert.That(result, Is.SameAs(effects));
        Assert.DoesNotThrow(() => store.AddBehavior(Array.Empty<SchedulingChoice>(), second,
            new ExecutionEffect[] { new AnnounceEffect(null, new Event(new PInt(7))) }, true));
    }

    [Test]
    public void ChoiceKindIdentityAndReceiveInputsRemainDistinct()
    {
        using var runtime = NewRuntime();
        using var otherRuntime = NewRuntime();
        var op = Operation(runtime);
        var node = new BehaviorStore.BehaviorNode(Array.Empty<ExecutionEffect>(), true);
        node.AddBehavior(Choice(4, op), Array.Empty<ExecutionEffect>(), true);
        Assert.That(node.Transitions.ContainsKey(Choice(4, op, payload: 9)), Is.False);
        Assert.That(node.Transitions.ContainsKey(Choice(4, op, receivedPayload: 9)), Is.False);
        Assert.That(node.Transitions.ContainsKey(Choice(4, op, initializing: true)), Is.False);
        Assert.That(node.Transitions.ContainsKey(Choice(2, op)), Is.False);
        Assert.That(node.Transitions.ContainsKey(Choice(4, Operation(runtime))), Is.False);
        Assert.That(node.Transitions.ContainsKey(Choice(4,
            Operation(otherRuntime, type: typeof(OtherMachine)))), Is.False);
    }

    [Test]
    public void EffectsCompareByValueAndPreserveOrderAndDestinations()
    {
        using var runtime = NewRuntime();
        var op = Operation(runtime);
        var sender = op.StateMachine.Id;
        var target = Operation(runtime).StateMachine.Id;
        var choice = Choice(0, op);
        var node = new BehaviorStore.BehaviorNode(Array.Empty<ExecutionEffect>(), true);
        ExecutionEffect[] Effects(int payload) => new ExecutionEffect[]
        {
            new SendEffect(sender, new Event(new PInt(payload)), target),
            new AnnounceEffect(sender, new Event(new PInt(3))),
            new CreateEffect(sender, target, null)
        };
        Assert.That(node.AddBehavior(choice, Effects(2), true), Is.True);
        Assert.That(node.AddBehavior(Choice(0, op), Effects(2), true), Is.True);
        Assert.That(node.AddBehavior(choice, Effects(4), true), Is.False);
        var changed = Effects(2);
        changed[0] = new SendEffect(sender, new Event(new PInt(2)), sender);
        Assert.That(node.AddBehavior(choice, changed, true), Is.False);
        changed = Effects(2);
        Array.Reverse(changed);
        Assert.That(node.AddBehavior(choice, changed, true), Is.False);
    }

    [Test]
    public void NullPayloadsAndEventTypesAreDistinguished()
    {
        using var runtime = NewRuntime();
        var op = Operation(runtime);
        var node = new BehaviorStore.BehaviorNode(Array.Empty<ExecutionEffect>(), true);
        node.AddBehavior(new InitializeChoice(op.Id, op.StateMachine.Id, new Event()), Array.Empty<ExecutionEffect>(), true);
        Assert.That(node.Transitions.ContainsKey(new InitializeChoice(op.Id, op.StateMachine.Id, new Event())), Is.True);
        Assert.That(node.Transitions.ContainsKey(new InitializeChoice(op.Id, op.StateMachine.Id, null)), Is.False);
        Assert.That(node.Transitions.ContainsKey(Choice(0, op)), Is.False);
        Assert.That(node.Transitions.ContainsKey(new InitializeChoice(op.Id, op.StateMachine.Id, new OtherEvent())), Is.False);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void ChoiceSnapshotRetainsOriginalInputAfterLivePayloadMutation(int kind)
    {
        using var runtime = NewRuntime();
        var op = Operation(runtime);
        var payload = new PSeq(new IPValue[] { new PSeq(new IPValue[] { new PInt(1) }) });
        var e = new OtherEvent { Payload = payload };
        var received = new Event(new PSeq(new IPValue[] { new PInt(2) }));
        SchedulingChoice MakeChoice() => kind switch
        {
            0 => new InitializeChoice(op.Id, op.StateMachine.Id, e),
            1 => new ResumeInitializationChoice(op.Id, op.StateMachine.Id, e),
            2 => new DeliverEventChoice(op.Id, op.StateMachine.Id, (e, null)),
            3 => new ResumeHandlerChoice(op.Id, op.StateMachine.Id, (e, null)),
            _ => new CompleteReceiveChoice(op.Id, op.StateMachine.Id, (e, null), (received, null), true)
        };
        var live = MakeChoice();
        var snapshot = live.Snapshot();
        var query = MakeChoice().Snapshot();
        var store = new BehaviorStore();
        store.AddBehavior(Array.Empty<SchedulingChoice>(), snapshot, Array.Empty<ExecutionEffect>(), true);

        ((PSeq)payload[0]).Add(new PInt(3));
        payload.Add(new PInt(4));
        ((PSeq)received.Payload).Add(new PInt(5));
        e.Payload = new PInt(6);

        Assert.That(store.GetBehavior(new List<SchedulingChoice> { query }, out _, out _), Is.True);
        Assert.That(store.GetBehavior(new List<SchedulingChoice> { live }, out _, out _), Is.False);
        Assert.That(snapshot.OperationId, Is.EqualTo(live.OperationId));
        Assert.That(snapshot.GetStateMachineId(), Is.SameAs(live.GetStateMachineId()));
    }

    [Test]
    public void EffectsSnapshotPayloadAtConstruction()
    {
        var nested = new PSeq(new IPValue[] { new PInt(1) });
        var payload = new PSeq(new IPValue[] { nested });
        var e = new OtherEvent { Payload = payload };
        var send = new SendEffect(null, e, null);
        var announce = new AnnounceEffect(null, e);
        nested.Add(new PInt(2));
        payload.Add(new PInt(3));
        e.Payload = null;

        foreach (var saved in new[] { send.SentEvent, announce.AnnouncedEvent })
        {
            Assert.That(saved, Is.TypeOf<OtherEvent>().And.Not.SameAs(e));
            Assert.That(((PSeq)saved.Payload).Count, Is.EqualTo(1));
            Assert.That(((PSeq)((PSeq)saved.Payload)[0]).Count, Is.EqualTo(1));
        }
        Assert.That(send.SentEvent.Payload, Is.Not.SameAs(announce.AnnouncedEvent.Payload));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void SenderIdentityDistinguishesDeliveryAndResumptionInputs(int kind)
    {
        using var runtime = NewRuntime();
        var receiver = Operation(runtime);
        var firstSender = Operation(runtime).StateMachine.Id;
        var secondSender = Operation(runtime).StateMachine.Id;
        var equivalentSender = Operation(runtime, path: firstSender.CreationPath).StateMachine.Id;
        var differentTypeSender = Operation(runtime, path: firstSender.CreationPath,
            type: typeof(OtherMachine)).StateMachine.Id;
        var differentInterfaceSender = Operation(runtime, name: "OtherInterface",
            path: firstSender.CreationPath).StateMachine.Id;
        SchedulingChoice Input(StateMachineId sender, string senderState)
        {
            var e = new Event(new PInt(1));
            var info = new EventInfo(e, new EventOriginInfo(sender, sender.Name, senderState),
                new VectorTime(sender));
            return kind switch
            {
                0 => new DeliverEventChoice(receiver.Id, receiver.StateMachine.Id, (e, info)),
                1 => new ResumeHandlerChoice(receiver.Id, receiver.StateMachine.Id, (e, info)),
                2 => new CompleteReceiveChoice(receiver.Id, receiver.StateMachine.Id, (e, info), (e, null), false),
                _ => new CompleteReceiveChoice(receiver.Id, receiver.StateMachine.Id, (e, null), (e, info), false)
            };
        }
        var node = new BehaviorStore.BehaviorNode(Array.Empty<ExecutionEffect>(), true);
        node.AddBehavior(Input(firstSender, "Before"), Array.Empty<ExecutionEffect>(), true);
        Assert.That(node.Transitions.ContainsKey(Input(firstSender, "After")), Is.True);
        Assert.That(node.Transitions.ContainsKey(Input(secondSender, "Before")), Is.False);
        Assert.That(node.Transitions.ContainsKey(Input(equivalentSender, "Before")), Is.True);
        Assert.That(BehaviorStoreComparers.ChoiceEquality.GetHashCode(Input(firstSender, "Before")),
            Is.EqualTo(BehaviorStoreComparers.ChoiceEquality.GetHashCode(Input(equivalentSender, "After"))));
        Assert.That(BehaviorStoreComparers.ChoiceEquality.Equals(
            Input(firstSender, "Before"), Input(differentTypeSender, "Before")), Is.False);
        Assert.That(node.Transitions.ContainsKey(Input(differentTypeSender, "Before")), Is.False);
        Assert.That(node.Transitions.ContainsKey(Input(differentInterfaceSender, "Before")), Is.False);
        Assert.That(node.AddBehavior(Input(differentTypeSender, "Before"),
            new ExecutionEffect[] { new AnnounceEffect(differentTypeSender, new Event(new PInt(7))) }, true), Is.True);
    }

    [Test]
    public void CreationComparisonUsesChildPathTypeAndPayload()
    {
        using var runtime = NewRuntime();
        var creator = Operation(runtime);
        var firstChild = Operation(runtime).StateMachine.Id;
        var secondChild = Operation(runtime).StateMachine.Id;
        var payload = new PSeq(new IPValue[] { new PInt(1) });
        var recorded = new CreateEffect(
            creator.StateMachine.Id, 
            firstChild, 
            new Event(payload));
        var choice = Choice(0, creator);
        var node = new BehaviorStore.BehaviorNode(Array.Empty<ExecutionEffect>(), true);
        node.AddBehavior(choice, new ExecutionEffect[] { recorded }, true);
        payload.Add(new PInt(2));

        var equivalentChild = Operation(runtime, path: firstChild.CreationPath).StateMachine.Id;
        var otherTypeChild = Operation(runtime, path: firstChild.CreationPath,
            type: typeof(OtherMachine)).StateMachine.Id;
        CreateEffect Creation(StateMachineId parent, StateMachineId child, int value) =>
            new CreateEffect(parent, child,
                new Event(new PSeq(new IPValue[] { new PInt(value) })));
        bool Matches(CreateEffect effect) => node.AddBehavior(choice, new ExecutionEffect[] { effect }, true);

        Assert.That(Matches(Creation(creator.StateMachine.Id, equivalentChild, 1)), Is.True);
        Assert.That(recorded.CreatedStateMachineId, Is.SameAs(firstChild));
        Assert.That(Matches(Creation(creator.StateMachine.Id, equivalentChild, 2)), Is.False);
        Assert.That(Matches(Creation(creator.StateMachine.Id, secondChild, 1)), Is.False);
        Assert.That(Matches(Creation(creator.StateMachine.Id, otherTypeChild, 1)), Is.False);
        Assert.That(Matches(Creation(firstChild, equivalentChild, 1)), Is.False);
    }

    private static MachineCreationPath Path(params uint[] parts) => new(parts);

    private static IPValue WrapReferences(int kind, IPValue a, IPValue b, bool reverse)
    {
        var ordered = new IPValue[] { a, b };
        switch (kind)
        {
            case 0: return a;
            case 1: return new PTuple(ordered);
            case 2: return new PNamedTuple(new[] { "first", "second" }, ordered);
            case 3: return new PSeq(ordered);
            case 4: return new PSet(new HashSet<IPValue>(reverse ? new[] { b, a } : ordered));
            case 5:
                var map = new Dictionary<IPValue, IPValue>();
                if (reverse)
                {
                    map.Add(new PTuple(b, new PInt(1)), new PSeq(new[] { a }));
                    map.Add(new PTuple(a, new PInt(1)), new PSeq(new[] { b }));
                }
                else
                {
                    map.Add(new PTuple(a, new PInt(1)), new PSeq(new[] { b }));
                    map.Add(new PTuple(b, new PInt(1)), new PSeq(new[] { a }));
                }
                return new PMap(map);
            default: return new PayloadEvent { Payload = new PTuple(ordered) };
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    public void NestedMachinePayloadsMatchAcrossSwappedRuntimeIds(int kind)
    {
        using var firstRuntime = NewRuntime();
        using var secondRuntime = NewRuntime();
        var a1 = Operation(firstRuntime, path: Path(0, 0)).StateMachine.Id;
        var b1 = Operation(firstRuntime, path: Path(0, 1)).StateMachine.Id;
        var b2 = Operation(secondRuntime, path: Path(0, 1)).StateMachine.Id;
        var a2 = Operation(secondRuntime, path: Path(0, 0)).StateMachine.Id;
        var receiver1 = Operation(firstRuntime, path: Path(0));
        Operation(secondRuntime); // Shift the receiver's runtime ID too.
        var receiver2 = Operation(secondRuntime, path: Path(0));
        PMachineValue Reference(StateMachineId id) => new(id, new List<string>());
        var firstEvent = new Event(WrapReferences(kind, Reference(a1), Reference(b1), false));
        var secondEvent = new Event(WrapReferences(kind, Reference(a2), Reference(b2), false));
        var first = new InitializeChoice(receiver1.Id, receiver1.StateMachine.Id, firstEvent);
        var second = new InitializeChoice(receiver2.Id, receiver2.StateMachine.Id, secondEvent);
        var comparer = BehaviorStoreComparers.ChoiceEquality;
        Assert.That(a1.Value, Is.EqualTo(b2.Value));
        Assert.That(comparer.Equals(first, second), Is.True);
        Assert.That(comparer.GetHashCode(first), Is.EqualTo(comparer.GetHashCode(second)));

        var effects = new ExecutionEffect[] { new SendEffect(receiver1.StateMachine.Id, firstEvent, a1) };
        var matchingEffects = new ExecutionEffect[] { new SendEffect(receiver2.StateMachine.Id, secondEvent, a2) };
        var store = new BehaviorStore();
        store.AddBehavior(Array.Empty<SchedulingChoice>(), first, effects, true);
        Assert.That(store.GetBehavior(new List<SchedulingChoice> { second }, out _, out _), Is.True);
        Assert.DoesNotThrow(() => store.AddBehavior(Array.Empty<SchedulingChoice>(), second, matchingEffects, true));
        Assert.That(BehaviorStoreComparers.EffectEquality.GetHashCode(effects[0]),
            Is.EqualTo(BehaviorStoreComparers.EffectEquality.GetHashCode(matchingEffects[0])));

        if (kind is 4 or 5)
        {
            var reorderedEvent = new Event(WrapReferences(kind, Reference(a2), Reference(b2), true));
            var reorderedChoice = new InitializeChoice(receiver2.Id, receiver2.StateMachine.Id, reorderedEvent);
            Assert.That(comparer.Equals(first, reorderedChoice), Is.False);
            Assert.That(store.GetBehavior(new List<SchedulingChoice> { reorderedChoice }, out _, out _), Is.False);
            Assert.That(BehaviorStoreComparers.EffectEquality.Equals(effects[0],
                new SendEffect(receiver2.StateMachine.Id, reorderedEvent, a2)), Is.False);
        }

        var c2 = Operation(secondRuntime, path: Path(0, 2)).StateMachine.Id;
        var wrongEvent = new Event(WrapReferences(kind, Reference(b2), Reference(c2), true));
        Assert.That(comparer.Equals(first, new InitializeChoice(receiver2.Id, receiver2.StateMachine.Id, wrongEvent)), Is.False);
    }

    private static IPValue CollectionPayload(bool map, params int[] order)
    {
        var values = order.Select(value => (IPValue)new PInt(value));
        return map
            ? new PMap(values.ToDictionary(value => value, value => value))
            : new PSet(new HashSet<IPValue>(values));
    }

    [TestCase(0)] // Set
    [TestCase(1)] // Map
    [TestCase(2)] // Set nested in a tuple
    [TestCase(3)] // Set used as a map key
    [TestCase(4)] // Map used as a map value
    public void ObservableCollectionOrderDistinguishesChoicesAndEffects(int kind)
    {
        using var runtime = NewRuntime();
        var op = Operation(runtime);
        IPValue Payload(params int[] order) => kind switch
        {
            0 => CollectionPayload(false, order),
            1 => CollectionPayload(true, order),
            2 => new PTuple(CollectionPayload(false, order)),
            3 => new PMap(new Dictionary<IPValue, IPValue>
                { [CollectionPayload(false, order)] = new PInt(0) }),
            _ => new PMap(new Dictionary<IPValue, IPValue>
                { [new PInt(0)] = CollectionPayload(true, order) })
        };
        SchedulingChoice Input(params int[] order) =>
            new InitializeChoice(op.Id, op.StateMachine.Id, new Event(Payload(order))).Snapshot();
        ExecutionEffect Effect(params int[] order) =>
            new AnnounceEffect(op.StateMachine.Id, new Event(Payload(order)));

        var forward = Input(1, 2);
        var reverse = Input(2, 1);
        var forwardEffect = Effect(1, 2);
        var reverseEffect = Effect(2, 1);
        Assert.That(BehaviorStoreComparers.ChoiceEquality.Equals(forward, reverse), Is.False);
        Assert.That(BehaviorStoreComparers.EffectEquality.Equals(forwardEffect, reverseEffect), Is.False);

        var store = new BehaviorStore();
        store.AddBehavior(Array.Empty<SchedulingChoice>(), forward, new[] { forwardEffect }, true);
        Assert.That(store.GetBehavior(new List<SchedulingChoice> { reverse }, out _, out _), Is.False);
        store.AddBehavior(Array.Empty<SchedulingChoice>(), reverse, new[] { reverseEffect }, true);

        foreach (var order in new[] { new[] { 1, 2 }, new[] { 2, 1 } })
        {
            Assert.That(store.GetBehavior(new List<SchedulingChoice> { Input(order) },
                out var effects, out var complete), Is.True);
            Assert.That(complete, Is.True);
            Assert.That(effects, Has.Count.EqualTo(1));
            Assert.That(BehaviorStoreComparers.EffectEquality.Equals(effects[0], Effect(order)), Is.True);
            Assert.That(BehaviorStoreComparers.EffectEquality.GetHashCode(effects[0]),
                Is.EqualTo(BehaviorStoreComparers.EffectEquality.GetHashCode(Effect(order))));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CollectionHashesReflectIterationOrder(bool map)
    {
        using var runtime = NewRuntime();
        var op = Operation(runtime);
        var choiceHashes = new HashSet<int>();
        var effectHashes = new HashSet<int>();
        var permutations = new[]
        {
            new[] { 1, 2, 3 }, new[] { 1, 3, 2 }, new[] { 2, 1, 3 },
            new[] { 2, 3, 1 }, new[] { 3, 1, 2 }, new[] { 3, 2, 1 }
        };
        foreach (var order in permutations)
        {
            var e = new Event(CollectionPayload(map, order));
            var choice = new InitializeChoice(op.Id, op.StateMachine.Id, e);
            var effect = new AnnounceEffect(op.StateMachine.Id, e);
            choiceHashes.Add(BehaviorStoreComparers.ChoiceEquality.GetHashCode(choice));
            effectHashes.Add(BehaviorStoreComparers.EffectEquality.GetHashCode(effect));
        }

        // Hash collisions are allowed, but all permutations must not systematically
        // collapse to one hash as they did with the unordered sum.
        Assert.That(choiceHashes.Count, Is.GreaterThan(1));
        Assert.That(effectHashes.Count, Is.GreaterThan(1));
    }

    [Test]
    public void OrderedFieldsAndMapAssociationsRemainSignificant()
    {
        using var runtime = NewRuntime();
        var op = Operation(runtime);
        bool Same(IPValue x, IPValue y) => BehaviorStoreComparers.ChoiceEquality.Equals(
            new InitializeChoice(op.Id, op.StateMachine.Id, new Event(x)), new InitializeChoice(op.Id, op.StateMachine.Id, new Event(y)));
        Assert.That(Same(new PTuple(new PInt(1), new PInt(2)),
            new PTuple(new PInt(2), new PInt(1))), Is.False);
        Assert.That(Same(new PSeq(new IPValue[] { new PInt(1), new PInt(2) }),
            new PSeq(new IPValue[] { new PInt(2), new PInt(1) })), Is.False);
        Assert.That(Same(new PNamedTuple(new[] { "a", "b" }, new PInt(1), new PInt(2)),
            new PNamedTuple(new[] { "b", "a" }, new PInt(1), new PInt(2))), Is.False);
        Assert.That(Same(new PMap(new Dictionary<IPValue, IPValue> { [new PInt(1)] = new PInt(2) }),
            new PMap(new Dictionary<IPValue, IPValue> { [new PInt(2)] = new PInt(1) })), Is.False);
        Assert.That(Same(new PayloadEvent { Payload = new PInt(1) },
            new PayloadEvent { Payload = new PInt(2) }), Is.False);
    }

    [Test]
    public void NullEffectSourcesCompareAndHashConsistently()
    {
        using var runtime = NewRuntime();
        var id = Operation(runtime).StateMachine.Id;
        var first = new CreateEffect(null, id, null);
        var second = new CreateEffect(null, id, null);
        var comparer = BehaviorStoreComparers.EffectEquality;
        Assert.That(comparer.Equals(first, second), Is.True);
        Assert.That(comparer.GetHashCode(first), Is.EqualTo(comparer.GetHashCode(second)));
        Assert.That(comparer.Equals(first, new CreateEffect(id, id, null)), Is.False);
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    [TestCase(0.0)]
    [TestCase(-0.0)]
    [TestCase(1.25)]
    public void FloatingPointObservationsMatchAcrossSnapshots(double value)
    {
        using var runtime = NewRuntime();
        var id = Operation(runtime).StateMachine.Id;
        // Nest the float to verify recursive comparison and hashing as well.
        Event Input() => new Event(new PTuple(new PFloat(value), new PInt(1)));
        var first = new InitializeChoice(id.Value, id, Input()).Snapshot();
        var second = new InitializeChoice(id.Value, id, Input()).Snapshot();
        ExecutionEffect[] Effects() => new ExecutionEffect[] { new AnnounceEffect(id, Input()) };
        var firstEffects = Effects();
        var secondEffects = Effects();
        Assert.That(BehaviorStoreComparers.ChoiceEquality.Equals(first, second), Is.True);
        Assert.That(BehaviorStoreComparers.ChoiceEquality.GetHashCode(first),
            Is.EqualTo(BehaviorStoreComparers.ChoiceEquality.GetHashCode(second)));
        Assert.That(BehaviorStoreComparers.EffectEquality.Equals(firstEffects[0], secondEffects[0]), Is.True);
        Assert.That(BehaviorStoreComparers.EffectEquality.GetHashCode(firstEffects[0]),
            Is.EqualTo(BehaviorStoreComparers.EffectEquality.GetHashCode(secondEffects[0])));

        var store = new BehaviorStore();
        store.AddBehavior(Array.Empty<SchedulingChoice>(), first, firstEffects, true);
        Assert.DoesNotThrow(() => store.AddBehavior(Array.Empty<SchedulingChoice>(), second, secondEffects, true));
        Assert.That(store.GetBehavior(new List<SchedulingChoice> { second }, out _, out var complete), Is.True);
        Assert.That(complete, Is.True);
    }

    [Test]
    public void SignedZerosHaveDistinctBehaviorKeysAndEffects()
    {
        using var runtime = NewRuntime();
        var id = Operation(runtime).StateMachine.Id;
        var positive = new PFloat(0.0);
        var negative = new PFloat(BitConverter.Int64BitsToDouble(long.MinValue));
        var forward = new InitializeChoice(id.Value, id, new Event(positive)).Snapshot();
        var reverse = new InitializeChoice(id.Value, id, new Event(negative)).Snapshot();
        var positiveEffect = new AnnounceEffect(id, new Event(new PFloat(1.0) / positive));
        var negativeEffect = new AnnounceEffect(id, new Event(new PFloat(1.0) / negative));
        Assert.That((double)(PFloat)positiveEffect.AnnouncedEvent.Payload, Is.EqualTo(double.PositiveInfinity));
        Assert.That((double)(PFloat)negativeEffect.AnnouncedEvent.Payload, Is.EqualTo(double.NegativeInfinity));
        Assert.That(BehaviorStoreComparers.ChoiceEquality.Equals(forward, reverse), Is.False);
        Assert.That(BehaviorStoreComparers.EffectEquality.Equals(
            new AnnounceEffect(id, new Event(positive)), new AnnounceEffect(id, new Event(negative))), Is.False);

        var store = new BehaviorStore();
        store.AddBehavior(Array.Empty<SchedulingChoice>(), forward, new ExecutionEffect[] { positiveEffect }, true);
        Assert.That(store.GetBehavior(new List<SchedulingChoice> { reverse }, out _, out _), Is.False);
        store.AddBehavior(Array.Empty<SchedulingChoice>(), reverse, new ExecutionEffect[] { negativeEffect }, true);
        Assert.That(store.GetBehavior(new List<SchedulingChoice> { forward }, out var effects, out _), Is.True);
        Assert.That(effects[0], Is.SameAs(positiveEffect));
        Assert.That(store.GetBehavior(new List<SchedulingChoice> { reverse }, out effects, out _), Is.True);
        Assert.That(effects[0], Is.SameAs(negativeEffect));
    }

    [Test]
    public void DistinctNaNRepresentationsRemainDistinct()
    {
        using var runtime = NewRuntime();
        var id = Operation(runtime).StateMachine.Id;
        var first = new PFloat(BitConverter.Int64BitsToDouble(0x7ff8000000000001L));
        var second = new PFloat(BitConverter.Int64BitsToDouble(0x7ff8000000000002L));
        Assert.That(double.IsNaN((double)first), Is.True);
        Assert.That(double.IsNaN((double)second), Is.True);
        Assert.That(BehaviorStoreComparers.ChoiceEquality.Equals(
            new InitializeChoice(id.Value, id, new Event(first)),
            new InitializeChoice(id.Value, id, new Event(second))), Is.False);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    public void InterfaceBindingsDistinguishNestedMachineReferences(int kind)
    {
        using var runtime = NewRuntime();
        var receiver = Operation(runtime);
        var first = Operation(runtime, name: "First", path: Path(0, 0)).StateMachine.Id;
        var equivalent = Operation(runtime, name: "First", path: Path(0, 0)).StateMachine.Id;
        var different = Operation(runtime, name: "Second", path: Path(0, 0)).StateMachine.Id;
        var other = Operation(runtime).StateMachine.Id;
        SchedulingChoice Input(StateMachineId id) => new InitializeChoice(receiver.Id, receiver.StateMachine.Id,
            new Event(WrapReferences(kind, new PMachineValue(id, new List<string>()),
                new PMachineValue(other, new List<string>()), false))).Snapshot();

        Assert.That(first.Value, Is.Not.EqualTo(equivalent.Value));
        Assert.That(first.InterfaceName, Is.EqualTo("I_First"));
        var comparer = BehaviorStoreComparers.ChoiceEquality;
        Assert.That(comparer.Equals(Input(first), Input(equivalent)), Is.True);
        Assert.That(comparer.GetHashCode(Input(first)), Is.EqualTo(comparer.GetHashCode(Input(equivalent))));
        Assert.That(comparer.Equals(Input(first), Input(different)), Is.False);
    }

    [Test]
    public void InterfaceBindingsDistinguishEffectSourcesDestinationsAndCreations()
    {
        using var runtime = NewRuntime();
        var first = Operation(runtime, name: "First", path: Path(0)).StateMachine.Id;
        var equivalent = Operation(runtime, name: "First", path: Path(0)).StateMachine.Id;
        var different = Operation(runtime, name: "Second", path: Path(0)).StateMachine.Id;
        var source = Operation(runtime).StateMachine.Id;
        ExecutionEffect[] Effects(StateMachineId id) => new ExecutionEffect[]
        {
            new AnnounceEffect(id, new Event()),
            new SendEffect(source, new Event(), id),
            new CreateEffect(source, id, null)
        };
        var originals = Effects(first);
        var matches = Effects(equivalent);
        var mismatches = Effects(different);
        var comparer = BehaviorStoreComparers.EffectEquality;
        for (int i = 0; i < originals.Length; i++)
        {
            Assert.That(comparer.Equals(originals[i], matches[i]), Is.True);
            Assert.That(comparer.GetHashCode(originals[i]), Is.EqualTo(comparer.GetHashCode(matches[i])));
            Assert.That(comparer.Equals(originals[i], mismatches[i]), Is.False);
        }
    }

    private static ExecutionEffect[] Announcements(params int[] payloads) =>
        payloads.Select(value => (ExecutionEffect)new AnnounceEffect(null, new Event(new PInt(value)))).ToArray();

    [TestCase(1, false, 2, true, 2, true)]
    [TestCase(2, true, 1, false, 2, true)]
    [TestCase(1, false, 2, false, 2, false)]
    [TestCase(2, false, 1, false, 2, false)]
    [TestCase(2, false, 2, true, 2, true)]
    [TestCase(2, true, 2, false, 2, true)]
    [TestCase(2, false, 2, false, 2, false)]
    [TestCase(2, true, 2, true, 2, true)]
    [TestCase(0, false, 2, true, 2, true)]
    [TestCase(2, true, 0, false, 2, true)]
    [TestCase(0, false, 2, false, 2, false)]
    [TestCase(2, false, 0, false, 2, false)]
    [TestCase(0, false, 0, true, 0, true)]
    [TestCase(0, true, 0, false, 0, true)]
    [TestCase(0, false, 0, false, 0, false)]
    [TestCase(0, true, 0, true, 0, true)]
    public void CompatibleObservationsKeepLongestPrefixAndCompletionStatus(
        int firstCount, bool firstComplete, int secondCount, bool secondComplete,
        int expectedCount, bool expectedComplete)
    {
        using var runtime = NewRuntime();
        var choice = Choice(0, Operation(runtime));
        var trace = new List<SchedulingChoice> { choice };
        var store = new BehaviorStore();
        var first = Announcements(Enumerable.Range(1, firstCount).ToArray());
        var second = Announcements(Enumerable.Range(1, secondCount).ToArray());
        store.AddBehavior(Array.Empty<SchedulingChoice>(), choice, first, firstComplete);

        Assert.That(store.GetBehavior(trace, out var initial, out var initiallyComplete), Is.True);
        Assert.That(initial, Is.SameAs(first));
        Assert.That(initiallyComplete, Is.EqualTo(firstComplete));

        store.AddBehavior(Array.Empty<SchedulingChoice>(), choice, second, secondComplete);

        Assert.That(store.GetBehavior(trace, out var effects, out var complete), Is.True);
        Assert.That(effects.SequenceEqual(
            Announcements(Enumerable.Range(1, expectedCount).ToArray()),
            BehaviorStoreComparers.EffectEquality), Is.True);
        Assert.That(complete, Is.EqualTo(expectedComplete));
    }

    [TestCase(2, true, 1, true)]
    [TestCase(1, true, 2, true)]
    [TestCase(2, false, 1, true)]
    [TestCase(1, true, 2, false)]
    [TestCase(0, true, 1, false)]
    [TestCase(1, false, 0, true)]
    public void ObservationsCannotExtendBeyondACompleteBehavior(
        int firstCount, bool firstComplete, int secondCount, bool secondComplete)
    {
        using var runtime = NewRuntime();
        var choice = Choice(0, Operation(runtime));
        var store = new BehaviorStore();
        var first = Announcements(Enumerable.Range(1, firstCount).ToArray());
        store.AddBehavior(Array.Empty<SchedulingChoice>(), choice, first, firstComplete);

        Assert.Throws<PInternalException>(() => store.AddBehavior(
            Array.Empty<SchedulingChoice>(), choice,
            Announcements(Enumerable.Range(1, secondCount).ToArray()), secondComplete));

        Assert.That(store.GetBehavior(new List<SchedulingChoice> { choice }, out var effects, out var complete), Is.True);
        Assert.That(effects, Is.SameAs(first), "A rejected observation must not modify the store.");
        Assert.That(complete, Is.EqualTo(firstComplete));
    }

    [Test]
    public void ConflictingPrefixesAreRejectedWithoutChangingTheStore(
        [Values(false, true)] bool firstComplete, [Values(false, true)] bool secondComplete)
    {
        using var runtime = NewRuntime();
        var choice = Choice(0, Operation(runtime));
        var store = new BehaviorStore();
        var first = Announcements(1, 2);
        store.AddBehavior(Array.Empty<SchedulingChoice>(), choice, first, firstComplete);

        Assert.Throws<PInternalException>(() => store.AddBehavior(
            Array.Empty<SchedulingChoice>(), choice, Announcements(1, 3), secondComplete));

        Assert.That(store.GetBehavior(new List<SchedulingChoice> { choice }, out var effects, out var complete), Is.True);
        Assert.That(effects, Is.SameAs(first));
        Assert.That(complete, Is.EqualTo(firstComplete));
    }

    [Test]
    public void CompletingAPrefixAllowsDescendantsAndLaterInterruptionsPreserveThem()
    {
        using var runtime = NewRuntime();
        var op = Operation(runtime);
        var first = Choice(0, op);
        var next = Choice(1, op);
        var store = new BehaviorStore();
        store.AddBehavior(Array.Empty<SchedulingChoice>(), first, Announcements(1), false);
        store.AddBehavior(Array.Empty<SchedulingChoice>(), first, Announcements(1, 2), true);
        store.AddBehavior(new[] { first }, next, Announcements(3), true);
        store.AddBehavior(Array.Empty<SchedulingChoice>(), first, Announcements(1), false);

        Assert.That(store.GetBehavior(new List<SchedulingChoice> { first, next },
            out var effects, out var complete), Is.True);
        Assert.That(effects.SequenceEqual(Announcements(3), BehaviorStoreComparers.EffectEquality), Is.True);
        Assert.That(complete, Is.True);

        Assert.That(store.GetBehavior(new List<SchedulingChoice> { first, Choice(2, op) },
            out effects, out complete), Is.False);
        Assert.That(effects, Is.Empty);
        Assert.That(complete, Is.False, "A missing trace must not be reported as complete.");
    }

    private sealed class PayloadEvent : Event
    {
        public override IPValue Clone() => new PayloadEvent { Payload = Payload?.Clone() };
    }

    private sealed class OtherMachine : StateMachine { }
    private sealed class TestMachine : StateMachine { }
    private sealed class OtherEvent : Event { }
}
