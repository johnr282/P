using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using PChecker.Configuration;
using PChecker.Generator.Object;
using PChecker.Runtime.StateMachines;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Strategies.Probabilistic;
using ControlledRuntime = PChecker.SystematicTesting.ControlledRuntime;
using Monitor = PChecker.Runtime.Specifications.Monitor;

namespace UnitTests;

[TestFixture]
public class SchedulingStrategyPortTests
{
    [Test]
    public void ProbabilisticRandomCanStayOnTheOperationAndSelectADifferentDelivery()
    {
        var random = RecordedRandom(1, 1);
        var strategy = new ProbabilisticRandomStrategy(10, 1, random);
        var receiver = ReceiverId();
        var previous = new DeliverEventChoice(0, receiver, default);
        var delivery = new DeliverEventChoice(0, receiver, default) { ChoiceId = 1 };
        var otherOperation = new RunTaskChoice(1);

        strategy.GetNextSchedulingChoice(
            previous, new SchedulingChoice[] { previous, delivery, otherOperation }, out var next);

        Assert.That(next, Is.SameAs(delivery));
        Assert.That(random.IntChoices.Pos, Is.EqualTo(2));
    }

    [Test]
    public void StandalonePctCountsInputsAndChangesPriorityBetweenOperations()
    {
        var strategy = new PCTStrategy(10, 1, RecordedRandom(0, 1, 2, 0, 1, 0));
        var receiver = ReceiverId();
        var first = new DeliverEventChoice(0, receiver, default);
        var second = new DeliverEventChoice(0, receiver, default) { ChoiceId = 1 };
        var otherOperation = new RunTaskChoice(1);

        strategy.GetNextSchedulingChoice(first, new[] { first }, out _);
        strategy.GetNextBooleanChoice(first, 2, out var booleanChoice);
        strategy.GetNextIntegerChoice(first, 3, out var integerChoice);
        Assert.That(booleanChoice, Is.True);
        Assert.That(integerChoice, Is.EqualTo(1));
        Assert.That(strategy.GetScheduledSteps(), Is.EqualTo(3));
        strategy.PrepareForNextIteration();

        strategy.GetNextSchedulingChoice(first, new[] { first, second }, out var next);
        Assert.That(next, Is.SameAs(second));
        strategy.GetNextSchedulingChoice(next, new SchedulingChoice[] { first, second, otherOperation }, out next);
        Assert.That(next, Is.SameAs(otherOperation));
    }

    [Test]
    public async Task TemperatureWrapperChecksInputChoicesAndForwardsInterruptedEffects()
    {
        await Task.Run(async () =>
        {
            var configuration = CheckerConfiguration.Create().WithLivenessTemperatureThreshold(1);
            var strategy = new RecordingStrategy();
            using var runtime = new ControlledRuntime(configuration, strategy);
            runtime.RunTest((Action<ControlledRuntime>)(r =>
            {
                r.TryCreateMonitor(typeof(HotMonitor));
                r.RandomBoolean();
                r.RandomBoolean();
            }), "hot-monitor");

            await runtime.WaitAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(runtime.Scheduler.BugReport, Does.Contain("potential liveness bug"));
            Assert.That(strategy.GetScheduledSteps(), Is.EqualTo(1));
            Assert.That(strategy.ReportedInterruptedExecution, Is.True);
        });
    }

    private static ControlledRandom RecordedRandom(params int[] choices) =>
        new(new Random(0)) { IntChoices = { Data = new List<int>(choices) } };

    private static StateMachineId ReceiverId() =>
        new(typeof(SchedulingStrategyPortTests), "receiver", null, null, useNameForHashing: true);

    public sealed class HotMonitor : Monitor
    {
        [Start]
        [Hot]
        private sealed class Initial : State { }
    }

    private sealed class RecordingStrategy : RandomStrategy
    {
        internal bool ReportedInterruptedExecution { get; private set; }

        internal RecordingStrategy() : base(10, RecordedRandom(0)) { }

        public override void NotifyEffects(
            SchedulingChoice lastChoice, IReadOnlyList<ExecutionEffect> effects, bool completeBehavior)
        {
            ReportedInterruptedExecution |= !completeBehavior;
        }
    }
}
