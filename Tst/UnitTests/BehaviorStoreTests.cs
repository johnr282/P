using System;
using System.Collections.Generic;
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
    private static ControlledRuntime NewRuntime()
    {
        var config = CheckerConfiguration.Create();
        return new ControlledRuntime(config, new RandomStrategy(10, new RandomValueGenerator(config)));
    }

    private static StateMachineOperation Operation(ControlledRuntime runtime, string name = "Machine")
    {
        var machine = new TestMachine();
        machine.Configure(runtime, new StateMachineId(typeof(TestMachine), name, null, runtime), null, null, null);
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
        Assert.That(node.Transitions.ContainsKey(Choice(4, Operation(otherRuntime, "Other"))), Is.False);
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
    public void CreationComparisonUsesRequestRatherThanAllocatedChildId()
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

        CreateEffect Creation(StateMachineId parent, Type type, string name, int value) =>
            new CreateEffect(parent, secondChild,
                new Event(new PSeq(new IPValue[] { new PInt(value) })));
        bool Matches(CreateEffect effect) => node.AddBehavior(choice, new ExecutionEffect[] { effect });

        Assert.That(Matches(Creation(creator.StateMachine.Id, typeof(TestMachine), "Child", 1)), Is.True);
        Assert.That(recorded.CreatedStateMachineId, Is.SameAs(firstChild));
        Assert.That(Matches(Creation(creator.StateMachine.Id, typeof(TestMachine), "Child", 2)), Is.False);
        Assert.That(Matches(Creation(creator.StateMachine.Id, typeof(TestMachine), "Other", 1)), Is.False);
        Assert.That(Matches(Creation(creator.StateMachine.Id, typeof(OtherMachine), "Child", 1)), Is.False);
        Assert.That(Matches(Creation(firstChild, typeof(TestMachine), "Child", 1)), Is.False);
    }

    private sealed class OtherMachine : StateMachine { }
    private sealed class TestMachine : StateMachine { }
    private sealed class OtherEvent : Event { }
}
