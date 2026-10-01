using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using PChecker.Configuration;
using PChecker.Random;
using PChecker.Runtime.Events;
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
            0 => new InitializeChoice(op, e),
            1 => new ResumeInitializationChoice(op, e),
            2 => new DeliverEventChoice(op, (e, new EventInfo(e))),
            3 => new ResumeHandlerChoice(op, (e, new EventInfo(e))),
            4 => new CompleteReceiveChoice(op, (e, null),
                (new Event(new PInt(receivedPayload)), null), initializing),
            _ => new RunTaskChoice(new TaskOperation(op.Id, null))
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
        store.AddBehavior(Array.Empty<SchedulingChoice>(), first, effects);

        Assert.That(store.GetBehavior(new List<SchedulingChoice> { second }, out var result), Is.True);
        Assert.That(result, Is.SameAs(effects));
        Assert.DoesNotThrow(() => store.AddBehavior(Array.Empty<SchedulingChoice>(), second,
            new ExecutionEffect[] { new AnnounceEffect(null, new Event(new PInt(7))) }));
    }

    [Test]
    public void ChoiceKindIdentityAndReceiveInputsRemainDistinct()
    {
        using var runtime = NewRuntime();
        using var otherRuntime = NewRuntime();
        var op = Operation(runtime);
        var node = new BehaviorStore.BehaviorNode(Array.Empty<ExecutionEffect>());
        node.AddBehavior(Choice(4, op), Array.Empty<ExecutionEffect>());
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
        var node = new BehaviorStore.BehaviorNode(Array.Empty<ExecutionEffect>());
        ExecutionEffect[] Effects(int payload) => new ExecutionEffect[]
        {
            new SendEffect(sender, new Event(new PInt(payload)), target),
            new AnnounceEffect(sender, new Event(new PInt(3))),
            new CreateEffect(sender, target, null)
        };
        Assert.That(node.AddBehavior(choice, Effects(2)), Is.True);
        Assert.That(node.AddBehavior(Choice(0, op), Effects(2)), Is.True);
        Assert.That(node.AddBehavior(choice, Effects(4)), Is.False);
        var changed = Effects(2);
        changed[0] = new SendEffect(sender, new Event(new PInt(2)), sender);
        Assert.That(node.AddBehavior(choice, changed), Is.False);
        changed = Effects(2);
        Array.Reverse(changed);
        Assert.That(node.AddBehavior(choice, changed), Is.False);
    }

    [Test]
    public void NullPayloadsAndEventTypesAreDistinguished()
    {
        using var runtime = NewRuntime();
        var op = Operation(runtime);
        var node = new BehaviorStore.BehaviorNode(Array.Empty<ExecutionEffect>());
        node.AddBehavior(new InitializeChoice(op, new Event()), Array.Empty<ExecutionEffect>());
        Assert.That(node.Transitions.ContainsKey(new InitializeChoice(op, new Event())), Is.True);
        Assert.That(node.Transitions.ContainsKey(new InitializeChoice(op, null)), Is.False);
        Assert.That(node.Transitions.ContainsKey(Choice(0, op)), Is.False);
        Assert.That(node.Transitions.ContainsKey(new InitializeChoice(op, new OtherEvent())), Is.False);
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
            0 => new InitializeChoice(op, e),
            1 => new ResumeInitializationChoice(op, e),
            2 => new DeliverEventChoice(op, (e, null)),
            3 => new ResumeHandlerChoice(op, (e, null)),
            _ => new CompleteReceiveChoice(op, (e, null), (received, null), true)
        };
        var live = MakeChoice();
        var snapshot = live.Snapshot();
        var query = MakeChoice().Snapshot();
        var store = new BehaviorStore();
        store.AddBehavior(Array.Empty<SchedulingChoice>(), snapshot, Array.Empty<ExecutionEffect>());

        ((PSeq)payload[0]).Add(new PInt(3));
        payload.Add(new PInt(4));
        ((PSeq)received.Payload).Add(new PInt(5));
        e.Payload = new PInt(6);

        Assert.That(store.GetBehavior(new List<SchedulingChoice> { query }, out _), Is.True);
        Assert.That(store.GetBehavior(new List<SchedulingChoice> { live }, out _), Is.False);
        Assert.That(snapshot.Operation, Is.SameAs(live.Operation));
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
        SchedulingChoice Input(StateMachineId sender, string senderState)
        {
            var e = new Event(new PInt(1));
            var info = new EventInfo(e, new EventOriginInfo(sender, sender.Name, senderState),
                new VectorTime(sender));
            return kind switch
            {
                0 => new DeliverEventChoice(receiver, (e, info)),
                1 => new ResumeHandlerChoice(receiver, (e, info)),
                2 => new CompleteReceiveChoice(receiver, (e, info), (e, null), false),
                _ => new CompleteReceiveChoice(receiver, (e, null), (e, info), false)
            };
        }
        var node = new BehaviorStore.BehaviorNode(Array.Empty<ExecutionEffect>());
        node.AddBehavior(Input(firstSender, "Before"), Array.Empty<ExecutionEffect>());
        Assert.That(node.Transitions.ContainsKey(Input(firstSender, "After")), Is.True);
        Assert.That(node.Transitions.ContainsKey(Input(secondSender, "Before")), Is.False);
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
        var node = new BehaviorStore.BehaviorNode(Array.Empty<ExecutionEffect>());
        node.AddBehavior(choice, new ExecutionEffect[] { recorded });
        payload.Add(new PInt(2));

        var equivalentChild = Operation(runtime, path: firstChild.CreationPath).StateMachine.Id;
        var otherTypeChild = Operation(runtime, path: firstChild.CreationPath,
            type: typeof(OtherMachine)).StateMachine.Id;
        CreateEffect Creation(StateMachineId parent, StateMachineId child, int value) =>
            new CreateEffect(parent, child,
                new Event(new PSeq(new IPValue[] { new PInt(value) })));
        bool Matches(CreateEffect effect) => node.AddBehavior(choice, new ExecutionEffect[] { effect });

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
        var secondEvent = new Event(WrapReferences(kind, Reference(a2), Reference(b2), true));
        var first = new InitializeChoice(receiver1, firstEvent);
        var second = new InitializeChoice(receiver2, secondEvent);
        var comparer = BehaviorStoreComparers.ChoiceEquality;
        Assert.That(a1.Value, Is.EqualTo(b2.Value));
        Assert.That(comparer.Equals(first, second), Is.True);
        Assert.That(comparer.GetHashCode(first), Is.EqualTo(comparer.GetHashCode(second)));

        var effects = new ExecutionEffect[] { new SendEffect(receiver1.StateMachine.Id, firstEvent, a1) };
        var matchingEffects = new ExecutionEffect[] { new SendEffect(receiver2.StateMachine.Id, secondEvent, a2) };
        var store = new BehaviorStore();
        store.AddBehavior(Array.Empty<SchedulingChoice>(), first, effects);
        Assert.That(store.GetBehavior(new List<SchedulingChoice> { second }, out _), Is.True);
        Assert.DoesNotThrow(() => store.AddBehavior(Array.Empty<SchedulingChoice>(), second, matchingEffects));
        Assert.That(BehaviorStoreComparers.EffectEquality.GetHashCode(effects[0]),
            Is.EqualTo(BehaviorStoreComparers.EffectEquality.GetHashCode(matchingEffects[0])));

        var c2 = Operation(secondRuntime, path: Path(0, 2)).StateMachine.Id;
        var wrongEvent = new Event(WrapReferences(kind, Reference(b2), Reference(c2), true));
        Assert.That(comparer.Equals(first, new InitializeChoice(receiver2, wrongEvent)), Is.False);
    }

    [Test]
    public void OrderedFieldsAndMapAssociationsRemainSignificant()
    {
        using var runtime = NewRuntime();
        var op = Operation(runtime);
        bool Same(IPValue x, IPValue y) => BehaviorStoreComparers.ChoiceEquality.Equals(
            new InitializeChoice(op, new Event(x)), new InitializeChoice(op, new Event(y)));
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

    private sealed class PayloadEvent : Event
    {
        public override IPValue Clone() => new PayloadEvent { Payload = Payload?.Clone() };
    }

    private sealed class OtherMachine : StateMachine { }
    private sealed class TestMachine : StateMachine { }
    private sealed class OtherEvent : Event { }
}
