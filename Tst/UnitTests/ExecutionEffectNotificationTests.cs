using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using PChecker.Configuration;
using PChecker.Random;
using PChecker.Runtime.Events;
using PChecker.Runtime.StateMachines;
using PChecker.Runtime.Values;
using PChecker.Exceptions;
using ControlledRuntime = PChecker.SystematicTesting.ControlledRuntime;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Strategies.Probabilistic;
using Monitor = PChecker.Runtime.Specifications.Monitor;

namespace UnitTests;

[TestFixture]
public class ExecutionEffectNotificationTests
{
    [Test]
    public void MonitorViolationReportsEffectBeforeFailureCallbackAndShutdown()
    {
        var configuration = CheckerConfiguration.Create();
        var strategy = new RecordingStrategy(configuration);
        using var runtime = new ControlledRuntime(configuration, strategy);
        var operation = new TaskOperation(0, runtime.Scheduler);
        runtime.Scheduler.RegisterOperation(operation);
        runtime.TryCreateMonitor(typeof(FailingMonitor));
        var choice = runtime.Scheduler.LastSchedulingChoice;
        var announcedEvent = new FailureEvent();
        var callbackInvoked = false;
        runtime.OnFailure += _ =>
        {
            callbackInvoked = true;
            Assert.That(strategy.Observations, Has.Count.EqualTo(1));
            Assert.That(runtime.Scheduler.WaitAsync().IsCompleted, Is.False);
        };

        Assert.Throws<ExecutionCanceledException>(() => runtime.Monitor<FailingMonitor>(announcedEvent));

        Assert.That(callbackInvoked, Is.True);
        Assert.That(strategy.Observations, Has.Count.EqualTo(1));
        Assert.That(strategy.Observations[0].Choice, Is.Not.SameAs(choice));
        Assert.That(strategy.Observations[0].Choice.Operation, Is.SameAs(choice.Operation));
        Assert.That(strategy.Observations[0].Effects, Has.Count.EqualTo(1));
        Assert.That(((AnnounceEffect)strategy.Observations[0].Effects[0]).AnnouncedEvent,
            Is.TypeOf<FailureEvent>().And.Not.SameAs(announcedEvent));
        Assert.That(runtime.Scheduler.WaitAsync().IsCompleted, Is.True);
        Assert.That(runtime.GetAndClearEffects(), Is.Empty);
    }

    [Test]
    public void ThrowingFailureCallbackCannotPreventNotification()
    {
        var configuration = CheckerConfiguration.Create();
        var strategy = new RecordingStrategy(configuration);
        using var runtime = new ControlledRuntime(configuration, strategy);
        runtime.Scheduler.RegisterOperation(new TaskOperation(0, runtime.Scheduler));
        runtime.TryCreateMonitor(typeof(FailingMonitor));
        var callbackFailure = new InvalidOperationException("Callback failed");
        runtime.OnFailure += _ => throw callbackFailure;

        Assert.That(Assert.Throws<InvalidOperationException>(
            () => runtime.Monitor<FailingMonitor>(new FailureEvent())), Is.SameAs(callbackFailure));
        Assert.That(strategy.Observations, Has.Count.EqualTo(1));
        Assert.That(strategy.Observations[0].Effects, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task NormalTerminationReportsEmptySegmentOnlyOnce()
    {
        var configuration = CheckerConfiguration.Create();
        var strategy = new RecordingStrategy(configuration);
        using var runtime = new ControlledRuntime(configuration, strategy);
        var operation = new TaskOperation(0, runtime.Scheduler);
        runtime.Scheduler.RegisterOperation(operation);
        operation.OnCompleted();

        // Run outside the runtime's root task, where scheduling requests are ignored.
        await Task.Run(() => Assert.Throws<ExecutionCanceledException>(() =>
            runtime.Scheduler.ScheduleNextEnabledOperation(AsyncOperationType.Stop)));

        Assert.That(strategy.Observations, Has.Count.EqualTo(1));
        Assert.That(strategy.Observations[0].Effects, Is.Empty);
        Assert.That(runtime.Scheduler.WaitAsync().IsCompleted, Is.True);
    }

    [Test]
    public void BoundCheckFlushesPendingEffectsWithoutAnotherSchedulingChoice()
    {
        var configuration = CheckerConfiguration.Create();
        var strategy = new RecordingStrategy(configuration);
        using var runtime = new ControlledRuntime(configuration, strategy);
        runtime.Scheduler.RegisterOperation(new TaskOperation(0, runtime.Scheduler));
        runtime.TryCreateMonitor(typeof(FailingMonitor));
        // A monitor input that does not violate the monitor, followed by a depth bound.
        runtime.Monitor<FailingMonitor>(new Event());
        strategy.ReachBound();

        Assert.Throws<ExecutionCanceledException>(() =>
            runtime.Scheduler.CheckIfSchedulingStepsBoundIsReached());

        Assert.That(strategy.Observations, Has.Count.EqualTo(1));
        Assert.That(strategy.Observations[0].Effects, Has.Count.EqualTo(1));
        Assert.That(runtime.Scheduler.WaitAsync().IsCompleted, Is.True);
    }

    public sealed class FailureEvent : Event { }

    [Test]
    public async Task InitializationObservationKeepsPayloadFromBeforeHandlerExecution()
    {
        await Task.Run(async () =>
        {
            var configuration = CheckerConfiguration.Create();
            var strategy = new RecordingStrategy(configuration, 100);
            using var runtime = new ControlledRuntime(configuration, strategy);
            var payload = new PSeq(new IPValue[] { new PInt(1) });
            var initialEvent = new Event(payload);
            runtime.RunTest((Action<ControlledRuntime>)(r =>
                r.CreateStateMachine(typeof(MutatingMachine), "MutatingMachine", initialEvent)), "snapshot");
            await runtime.WaitAsync().WaitAsync(TimeSpan.FromSeconds(10));

            var observation = strategy.Observations.Find(o => o.Choice is InitializeChoice);
            Assert.That(observation.Choice, Is.Not.Null, runtime.Scheduler.BugReport);
            var snapshot = ((InitializeChoice)observation.Choice).InitialEvent;
            Assert.That(snapshot, Is.Not.SameAs(initialEvent));
            Assert.That(((PSeq)snapshot.Payload).Count, Is.EqualTo(1));
            Assert.That(payload.Count, Is.EqualTo(2), "The handler must still receive the live event.");
        });
    }

    public sealed class MutatingMachine : StateMachine
    {
        [Start]
        [OnEntry(nameof(Mutate))]
        private sealed class Initial : State { }

        private void Mutate(Event e)
        {
            ((PSeq)e.Payload).Add(new PInt(2));
            Runtime.Assert(false, "Expected failure after mutation");
        }
    }

    public sealed class FailingMonitor : Monitor
    {
        [Start]
        [OnEventDoAction(typeof(FailureEvent), nameof(Fail))]
        [OnEventDoAction(typeof(Event), nameof(Ignore))]
        private sealed class Initial : State { }

        private void Fail(Event e) => Assert(false, "Expected monitor violation");
        private void Ignore(Event e) { }
    }

    private sealed class RecordingStrategy : RandomStrategy
    {
        internal readonly List<(SchedulingChoice Choice, IReadOnlyList<ExecutionEffect> Effects)>
            Observations = new();

        internal RecordingStrategy(CheckerConfiguration configuration, int maxSteps = 1)
            : base(maxSteps, new RandomValueGenerator(configuration)) { }

        internal void ReachBound() => ScheduledSteps = 1;

        public override void NotifyEffects(SchedulingChoice lastChoice, IReadOnlyList<ExecutionEffect> effects)
            => Observations.Add((lastChoice, effects));
    }
}
