using System.Linq;
using NUnit.Framework;
using PChecker.Configuration;
using PChecker.Feedback;
using PChecker.Generator.Object;
using PChecker.Runtime.Events;
using PChecker.Runtime.StateMachines;
using PChecker.SystematicTesting.Strategies.Feedback;
using PChecker.SystematicTesting.Strategies.Probabilistic;
using PChecker.SystematicTesting.Traces;

namespace UnitTests;

[TestFixture]
public class FeedbackGuidedStrategyTests
{
    [Test]
    public void FestTracksNovelTimelinesPerCampaign()
    {
        var timeline = new TimelineObserver();
        var trace = new ScheduleTrace();
        var configuration = CheckerConfiguration.Create();
        var firstStrategy = new FestStrategy(configuration, new ControlledRandom(configuration),
            new RandomScheduler(new ControlledRandom(configuration)));
        var secondStrategy = new FestStrategy(configuration, new ControlledRandom(configuration),
            new RandomScheduler(new ControlledRandom(configuration)));

        firstStrategy.ObserveRunningResults(timeline, trace);
        firstStrategy.ObserveRunningResults(timeline, trace);
        secondStrategy.ObserveRunningResults(timeline, trace);

        Assert.That(firstStrategy.TotalSavedInputs(), Is.EqualTo(1));
        Assert.That(secondStrategy.TotalSavedInputs(), Is.EqualTo(1));
    }

    [Test]
    public void DiversityComparesWithAdmittedTimelines()
    {
        var empty = new TimelineObserver();
        var nonEmpty = new TimelineObserver();
        var machine = new StateMachineId(typeof(FeedbackGuidedStrategyTests), "test", null, null,
            useNameForHashing: true);
        nonEmpty.OnDequeueEvent(machine, "state", DefaultEvent.Instance);
        nonEmpty.OnDequeueEvent(machine, "state", DefaultEvent.Instance);

        var diversity = new TimelineDiversity();
        var (initialScore, minHash) = diversity.Measure(empty);
        Assert.That(initialScore, Is.EqualTo(20));
        Assert.That(diversity.Measure(nonEmpty).Score, Is.EqualTo(20));

        diversity = new TimelineDiversity();
        diversity.RecordAdmission(diversity.Measure(empty).MinHash);
        var distance = minHash.Zip(nonEmpty.GetTimelineMinhash()).Count(pair => pair.First != pair.Second);
        Assert.That(distance, Is.Positive);
        Assert.That(diversity.Measure(nonEmpty).Score, Is.EqualTo(20 + distance));
    }

    [Test]
    public void MutationReceivesFeedbackFromAnAdmittedDuplicateTimeline()
    {
        var configuration = CheckerConfiguration.Create();
        var strategy = new DuplicateTimelineStrategy(configuration, new ControlledRandom(configuration),
            new RandomScheduler(new ControlledRandom(configuration)));

        strategy.ObserveRunningResults(new TimelineObserver(), new ScheduleTrace());
        Assert.That(strategy.TotalSavedInputs(), Is.Zero);

        strategy.ObserveRunningResults(new TimelineObserver(), new ScheduleTrace());
        Assert.That(strategy.TotalSavedInputs(), Is.EqualTo(1));

        strategy.PrepareForNextIteration();
        Assert.That(strategy.Parent?.Feedback, Is.SameAs(strategy.DuplicateFeedback));
    }

    private sealed class DuplicateTimelineStrategy : FeedbackGuidedStrategy
    {
        private readonly TimelineDiversity _diversity = new();
        internal DuplicateTimelineStrategy(CheckerConfiguration configuration, ControlledRandom input, IScheduler scheduler)
            : base(configuration, input, scheduler)
        {
        }

        internal SeedFeedback DuplicateFeedback { get; } = new(10, 1);

        internal GeneratorRecord Parent { get; private set; }

        protected override SeedFeedback ComputeFeedback(TimelineObserver timeline, ScheduleTrace scheduleTrace)
        {
            var (score, minHash) = _diversity.Measure(timeline);
            if (score != 0)
            {
                return null;
            }

            _diversity.RecordAdmission(minHash);
            return DuplicateFeedback;
        }

        protected override StrategyGenerator MutateRecording(GeneratorRecord parent)
        {
            Parent = parent;
            return new StrategyGenerator(parent.Generator.InputGenerator.Mutate(), parent.Generator.Scheduler.Mutate());
        }
    }
}
