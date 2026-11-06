using System.Collections.Generic;
using NUnit.Framework;
using PChecker.Generator.Object;
using PChecker.Runtime.StateMachines;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Strategies.Probabilistic;

namespace UnitTests;

[TestFixture]
public class FeedbackSchedulerTests
{
    [Test]
    public void RandomDrawsOneIndexFromTheFullChoiceList()
    {
        var random = RecordedRandom(0, 1);
        var scheduler = new RandomScheduler(random);
        var receiver = MachineId("receiver");
        var first = new DeliverEventChoice(0, receiver, default);
        var second = new DeliverEventChoice(0, receiver, default) { ChoiceId = 1 };
        var otherOperation = new RunTaskChoice(1);

        scheduler.GetNextSchedulingChoice(first, new[] { first }, out var next);
        Assert.That(next, Is.SameAs(first));
        Assert.That(random.IntChoices.Pos, Is.EqualTo(1));

        scheduler.GetNextSchedulingChoice(first, new SchedulingChoice[] { first, second, otherOperation }, out next);
        Assert.That(next, Is.SameAs(second));
        Assert.That(random.IntChoices.Pos, Is.EqualTo(2));
    }

    [Test]
    public void PctDefersPriorityChangeUntilTwoOperationsCanRun()
    {
        var scheduler = new PCTScheduler(1, 2, RecordedRandom(1, 1, 0));
        var receiver = MachineId("receiver");
        var first = new DeliverEventChoice(0, receiver, default);
        var second = new DeliverEventChoice(0, receiver, default) { ChoiceId = 1 };
        var otherOperation = new RunTaskChoice(1);

        scheduler.GetNextSchedulingChoice(first, new[] { first, second }, out var next);
        Assert.That(next, Is.SameAs(second));

        scheduler.GetNextSchedulingChoice(next, new SchedulingChoice[] { first, second, otherOperation }, out next);
        Assert.That(next, Is.SameAs(otherOperation));
    }

    [Test]
    public void PosFavorsNonSendersAndResetsOnlySendersToTheChosenTarget()
    {
        var random = RecordedRandom(0, 0, 0, 0, 2);
        var scheduler = new POSScheduler(random);
        var current = new RunTaskChoice(0);
        var nonSender = new RunTaskChoice(4);
        var first = new ResumeHandlerChoice(1, MachineId("first"), default, MachineId("receiver"));
        var second = new ResumeHandlerChoice(2, MachineId("second"), default, MachineId("receiver"));
        var third = new ResumeHandlerChoice(3, MachineId("third"), default, MachineId("otherReceiver"));

        scheduler.GetNextSchedulingChoice(current, new SchedulingChoice[] { nonSender, third, second, first }, out var next);
        Assert.That(next, Is.SameAs(nonSender));

        scheduler.GetNextSchedulingChoice(next, new[] { first, second, third }, out next);
        Assert.That(next, Is.SameAs(first));
        Assert.That(((StateMachineSchedulingChoice)next.Snapshot()).PendingSendTarget, Is.EqualTo(first.PendingSendTarget));

        scheduler.GetNextSchedulingChoice(next, new[] { second, third }, out next);
        Assert.That(next, Is.SameAs(third));
        Assert.That(random.IntChoices.Pos, Is.EqualTo(5));
    }

    private static ControlledRandom RecordedRandom(params int[] choices) =>
        new(new System.Random(0)) { IntChoices = { Data = new List<int>(choices) } };

    private static StateMachineId MachineId(string name) =>
        new(typeof(FeedbackSchedulerTests), name, null, null, useNameForHashing: true);
}
