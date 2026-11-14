using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using PChecker.Configuration;
using PChecker.Feedback;
using PChecker.Generator.Object;
using PChecker.Runtime.Events;
using PChecker.Runtime.Values;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Strategies.Feedback;
using PChecker.SystematicTesting.Strategies.Feedback.MonitorGuided;
using PChecker.SystematicTesting.Strategies.MonitorGuided;
using PChecker.SystematicTesting.Strategies.Probabilistic;
using Plang.Compiler;
using ControlledRuntime = PChecker.SystematicTesting.ControlledRuntime;
using Machine = Plang.Compiler.TypeChecker.AST.Declarations.Machine;
using Monitor = PChecker.Runtime.Specifications.Monitor;

namespace UnitTests;

[TestFixture]
public class MonitorGuidedFeedbackStrategyTests
{
    [Test]
    public async Task ReceivesBatchedMonitorEffectsAndResetsBetweenIterations()
    {
        var ast = new Machine("Target", new PParser.SpecMachineDeclContext(null, 0));
        var configuration = CheckerConfiguration.Create();
        var strategy = new TestStrategy(configuration, ast);

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
            Assert.That(runtime.Scheduler.ScheduleTrace.Count, Is.EqualTo(2));
        });

        strategy.ObserveRunningResults(new TimelineObserver());
        Assert.That(strategy.Observations, Has.Count.EqualTo(1));
        var observation = strategy.Observations[0];
        Assert.That(observation.MonitorName, Is.EqualTo("Target"));
        Assert.That(observation.ObservedEvent.Payload, Is.EqualTo(new PInt(7)));
        Assert.That(observation.Ignored, Is.True);
        Assert.That(observation.TracePosition, Is.EqualTo(1));

        strategy.PrepareForNextIteration();
        strategy.ObserveRunningResults(new TimelineObserver());
        Assert.That(strategy.Observations, Is.Empty);
    }

    private sealed class TestStrategy : MonitorGuidedFeedbackStrategy
    {
        internal TestStrategy(CheckerConfiguration configuration, Machine ast)
            : base(configuration, new ControlledRandom(configuration),
                new RandomScheduler(new ControlledRandom(configuration)), ast)
        {
        }

        internal List<MonitorObservationEffect> Observations { get; } = new();

        protected override void ResetMonitorProgress() => Observations.Clear();

        protected override void ObserveMonitorEvent(MonitorObservationEffect observation, MonitorGuidance guidance) =>
            Observations.Add(observation);

        protected override SeedFeedback ComputeFeedback(TimelineObserver timeline, MonitorGuidance guidance) => null;

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
