using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using PChecker.Configuration;
using PChecker.Random;
using PChecker.Runtime;
using PChecker.Runtime.Events;
using PChecker.Runtime.StateMachines;
using PChecker.Runtime.Values;
using PChecker.Exceptions;
using ControlledRuntime = PChecker.SystematicTesting.ControlledRuntime;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Strategies.MonitorGuided;
using PChecker.SystematicTesting.Strategies.MonitorGuided.Predictors;
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
            Assert.That(strategy.Observations[0].CompleteBehavior, Is.False);
            Assert.That(runtime.Scheduler.WaitAsync().IsCompleted, Is.False);
        };

        Assert.Throws<ExecutionCanceledException>(() => runtime.Monitor<FailingMonitor>(announcedEvent));

        Assert.That(callbackInvoked, Is.True);
        Assert.That(strategy.Observations, Has.Count.EqualTo(1));
        Assert.That(strategy.Observations[0].Choice, Is.Not.SameAs(choice));
        Assert.That(strategy.Observations[0].Choice.OperationId, Is.EqualTo(choice.OperationId));
        Assert.That(strategy.Observations[0].Effects, Has.Count.EqualTo(1));
        Assert.That(strategy.Observations[0].CompleteBehavior, Is.False);
        Assert.That(((MonitorObservationEffect)strategy.Observations[0].Effects[0]).ObservedEvent,
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
        Assert.That(strategy.Observations[0].CompleteBehavior, Is.False);
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
        Assert.That(strategy.Observations[0].CompleteBehavior, Is.True);
        Assert.That(runtime.Scheduler.WaitAsync().IsCompleted, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BoundCheckFlushesPendingEffectsWithoutAnotherSchedulingChoice(bool considerBoundAsBug)
    {
        var configuration = CheckerConfiguration.Create();
        configuration.ConsiderDepthBoundHitAsBug = considerBoundAsBug;
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
        Assert.That(strategy.Observations[0].CompleteBehavior, Is.False);
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
            Assert.That(observation.CompleteBehavior, Is.False);
            var snapshot = ((InitializeChoice)observation.Choice).InitialEvent;
            Assert.That(snapshot, Is.Not.SameAs(initialEvent));
            Assert.That(((PSeq)snapshot.Payload).Count, Is.EqualTo(1));
            Assert.That(payload.Count, Is.EqualTo(2), "The handler must still receive the live event.");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExactPredictorAcceptsInterruptedAndCompleteRunsInEitherOrder(bool interruptedFirst)
    {
        var predictor = new ExactPredictor();
        SchedulingChoice recordedChoice = null;
        foreach (var interrupt in new[] { interruptedFirst, !interruptedFirst })
        {
            // RunTest requires Task.CurrentId, which an await continuation need not retain.
            await Task.Run(async () =>
            {
                var configuration = CheckerConfiguration.Create();
                var strategy = new RecordingStrategy(configuration, 100);
                using var runtime = new ControlledRuntime(configuration, strategy);
                runtime.TryCreateMonitor(typeof(ConditionalFailureMonitor));
                if (interrupt)
                {
                    // Model a different monitor state without changing the machine's inputs.
                    runtime.Monitor<ConditionalFailureMonitor>(new ArmFailureEvent());
                }

                runtime.RunTest((Action<ControlledRuntime>)(r =>
                    r.CreateStateMachine(typeof(TwoAnnouncementsMachine), "TwoAnnouncements")), "interruption");
                await runtime.WaitAsync().WaitAsync(TimeSpan.FromSeconds(10));

                Assert.That(runtime.Scheduler.BugFound, Is.EqualTo(interrupt), runtime.Scheduler.BugReport);
                var initializations = strategy.Observations.FindAll(o => o.Choice is InitializeChoice);
                Assert.That(initializations, Has.Count.EqualTo(1), "The final segment must be reported exactly once.");
                var observation = initializations[0];
                Assert.That(observation.CompleteBehavior, Is.EqualTo(!interrupt));
                Assert.That(observation.Effects, Has.Count.EqualTo(interrupt ? 1 : 3));
                Assert.That(((PInt)((MonitorObservationEffect)observation.Effects[0]).ObservedEvent.Payload).Equals(new PInt(1)), Is.True);

                // Record the runtime observations directly in the predictor.
                foreach (var recorded in strategy.Observations)
                {
                    var creationPath = recorded.Choice.GetCreationPath();
                    if (creationPath == null)
                    {
                        continue;
                    }

                    recordedChoice ??= recorded.Choice;
                    Assert.DoesNotThrow(() => predictor.AddObservation(
                        creationPath,
                        Array.Empty<SchedulingChoice>(),
                        recorded.Choice,
                        recorded.Effects,
                        recorded.CompleteBehavior));
                }
            });
        }

        Assert.That(predictor.PredictEffects(
            recordedChoice.GetCreationPath(),
            Array.Empty<SchedulingChoice>(),
            recordedChoice,
            out var predictedEffects), Is.True);
        Assert.That(predictedEffects, Has.Count.EqualTo(3));
        Assert.That(predictedEffects[0], Is.TypeOf<MonitorObservationEffect>());
        Assert.That(predictedEffects[1], Is.TypeOf<MonitorObservationEffect>());
        Assert.That(predictedEffects[2], Is.TypeOf<CompleteHandlerEffect>());
    }

    [TestCase(false)]
    [TestCase(true)]
    [NonParallelizable]
    public async Task InterfaceBindingsKeepDifferentMonitorObservationsSeparate(bool monitoredFirst)
    {
        var originalMonitorMap = PModule.monitorMap;
        var originalObserves = PModule.monitorObserves;
        try
        {
            PModule.monitorMap = new()
            {
                ["I_Monitored"] = new() { typeof(FailingMonitor) },
                ["I_Unmonitored"] = new()
            };
            PModule.monitorObserves = new() { [nameof(FailingMonitor)] = new() { nameof(Event) } };
            var store = new BehaviorStore();
            SchedulingChoice firstChoice = null;
            // Revisit the first interface to verify reuse across fresh runtime IDs too.
            foreach (var monitored in new[] { monitoredFirst, !monitoredFirst, monitoredFirst })
            {
                await Task.Run(async () =>
                {
                    var configuration = CheckerConfiguration.Create();
                    var strategy = new RecordingStrategy(configuration, 100);
                    using var runtime = new ControlledRuntime(configuration, strategy);
                    runtime.TryCreateMonitor(typeof(FailingMonitor));
                    var alias = monitored ? "Monitored" : "Unmonitored";
                    runtime.RunTest((Action<ControlledRuntime>)(r =>
                        r.CreateStateMachine(typeof(InterfaceAnnouncementMachine), alias)), "interface-binding");
                    await runtime.WaitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.That(runtime.Scheduler.BugFound, Is.False, runtime.Scheduler.BugReport);
                    var initializations = strategy.Observations.FindAll(o => o.Choice is InitializeChoice);
                    Assert.That(initializations, Has.Count.EqualTo(1));
                    var sample = initializations[0];
                    Assert.That(sample.Choice.GetStateMachineId().InterfaceName, Is.EqualTo("I_" + alias));
                    Assert.That(sample.CompleteBehavior, Is.True);
                    Assert.That(sample.Effects, Has.Count.EqualTo(monitored ? 2 : 1));
                    Assert.That(sample.Effects[0], Is.TypeOf(monitored
                        ? typeof(MonitorObservationEffect)
                        : typeof(CompleteHandlerEffect)));
                    if (firstChoice != null && monitored != monitoredFirst)
                    {
                        Assert.That(sample.Choice.GetCreationPath(), Is.EqualTo(firstChoice.GetCreationPath()));
                        Assert.That(BehaviorStoreComparers.ChoiceEquality.Equals(firstChoice, sample.Choice), Is.False);
                        Assert.That(store.GetBehavior(Array.Empty<SchedulingChoice>(), sample.Choice, out _, out _), Is.False);
                    }
                    firstChoice ??= sample.Choice;
                    store.AddBehavior(Array.Empty<SchedulingChoice>(), sample.Choice, sample.Effects, sample.CompleteBehavior);
                    Assert.That(store.GetBehavior(Array.Empty<SchedulingChoice>(), sample.Choice,
                        out var effects, out var complete), Is.True);
                    Assert.That(effects, Has.Count.EqualTo(monitored ? 2 : 1));
                    Assert.That(complete, Is.True);
                });
            }
        }
        finally
        {
            PModule.monitorMap = originalMonitorMap;
            PModule.monitorObserves = originalObserves;
        }
    }

    public sealed class InterfaceAnnouncementMachine : StateMachine
    {
        [Start]
        [OnEntry(nameof(AnnounceThroughInterface))]
        private sealed class Initial : State { }

        private void AnnounceThroughInterface(Event e) => AnnounceInternal(new Event(new PInt(1)));
    }

    public sealed class ArmFailureEvent : Event { }

    public sealed class ConditionalFailureMonitor : Monitor
    {
        private bool fail;

        [Start]
        [OnEventDoAction(typeof(ArmFailureEvent), nameof(Arm))]
        [OnEventDoAction(typeof(Event), nameof(Observe))]
        private sealed class Initial : State { }

        private void Arm(Event e) => fail = true;
        private void Observe(Event e) => Assert(!fail, "Expected interruption at first announcement");
    }

    public sealed class TwoAnnouncementsMachine : StateMachine
    {
        [Start]
        [OnEntry(nameof(AnnounceTwice))]
        private sealed class Initial : State { }

        private void AnnounceTwice(Event e)
        {
            Monitor<ConditionalFailureMonitor>(new Event(new PInt(1)));
            Monitor<ConditionalFailureMonitor>(new Event(new PInt(2)));
        }
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
        internal readonly List<(SchedulingChoice Choice, IReadOnlyList<ExecutionEffect> Effects, bool CompleteBehavior)>
            Observations = new();

        internal RecordingStrategy(CheckerConfiguration configuration, int maxSteps = 1)
            : base(maxSteps, new RandomValueGenerator(configuration)) { }

        internal void ReachBound() => ScheduledSteps = 1;

        public override void NotifyEffects(SchedulingChoice lastChoice, IReadOnlyList<ExecutionEffect> effects,
            bool completeBehavior)
            => Observations.Add((lastChoice, effects, completeBehavior));
    }
}
