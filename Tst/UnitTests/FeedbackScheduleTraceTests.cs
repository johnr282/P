using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using PChecker.Configuration;
using PChecker.Feedback;
using PChecker.Generator.Object;
using PChecker.Runtime.Events;
using PChecker.Runtime.Values;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Strategies.Feedback;
using PChecker.SystematicTesting.Strategies.Probabilistic;
using PChecker.SystematicTesting.Traces;
using ControlledRuntime = PChecker.SystematicTesting.ControlledRuntime;
using Monitor = PChecker.Runtime.Specifications.Monitor;

namespace UnitTests;

[TestFixture]
public class FeedbackScheduleTraceTests
{
    [Test]
    public async Task FeedbackReceivesCompletedTraceWithObservationPositions()
    {
        var configuration = CheckerConfiguration.Create();
        var strategy = new TestStrategy(configuration);

        await Task.Run(async () =>
        {
            using var runtime = new ControlledRuntime(configuration, strategy);
            runtime.RunTest((Action<ControlledRuntime>)(r =>
            {
                r.RegisterMonitor<OtherMonitor>("Other");
                r.RegisterMonitor<TargetMonitor>("Target");
                r.Monitor<OtherMonitor>(new Event(new PInt(3)));
                r.RandomBoolean();
                r.Monitor<TargetMonitor>(new Event(new PInt(7)));
                r.RandomBoolean();
            }), "monitor-effects");
            await runtime.WaitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(runtime.Scheduler.BugFound, Is.False, runtime.Scheduler.BugReport);
            var trace = runtime.Scheduler.ScheduleTrace;
            strategy.ObserveRunningResults(new TimelineObserver(), trace);
            Assert.That(strategy.FeedbackTrace, Is.SameAs(trace));
            Assert.That(strategy.FeedbackTrace.Count, Is.EqualTo(2));
            Assert.That(strategy.FeedbackTrace.All(step => step.BooleanChoice.HasValue), Is.True);
        });

        Assert.That(strategy.Observations.Select(observation => observation.MonitorName),
            Is.EqualTo(new[] { "Other", "Target" }));
        var observation = strategy.Observations[1];
        Assert.That(observation.MonitorName, Is.EqualTo("Target"));
        Assert.That(observation.ObservedEvent.Payload, Is.EqualTo(new PInt(7)));
        Assert.That(observation.Ignored, Is.True);
        Assert.That(observation.TracePosition, Is.EqualTo(1));
    }

    private sealed class TestStrategy : FeedbackGuidedStrategy
    {
        internal TestStrategy(CheckerConfiguration configuration)
            : base(configuration, new ControlledRandom(configuration),
                new RandomScheduler(new ControlledRandom(configuration)))
        {
        }

        internal List<MonitorObservationEffect> Observations { get; } = new();
        internal ScheduleTrace FeedbackTrace { get; private set; }

        public override void NotifyEffects(SchedulingChoice lastChoice, IReadOnlyList<ExecutionEffect> effects,
            bool completeBehavior) => Observations.AddRange(effects.OfType<MonitorObservationEffect>());

        protected override SeedFeedback ComputeFeedback(TimelineObserver timeline, ScheduleTrace scheduleTrace)
        {
            FeedbackTrace = scheduleTrace;
            return null;
        }

        protected override StrategyGenerator MutateRecording(GeneratorRecord parent) =>
            throw new NotSupportedException();
    }

    public sealed class TargetMonitor : Monitor
    {
        [Start]
        [IgnoreEvents(typeof(Event))]
        private sealed class Initial : State { }
    }

    public sealed class OtherMonitor : Monitor
    {
        [Start]
        [IgnoreEvents(typeof(Event))]
        private sealed class Initial : State { }
    }
}
