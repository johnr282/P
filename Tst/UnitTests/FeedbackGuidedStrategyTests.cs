using System.Linq;
using NUnit.Framework;
using PChecker.Configuration;
using PChecker.Feedback;
using PChecker.Generator.Object;
using PChecker.Runtime.Events;
using PChecker.Runtime.StateMachines;
using PChecker.SystematicTesting.Strategies.Feedback;
using PChecker.SystematicTesting.Strategies.Probabilistic;

namespace UnitTests;

[TestFixture]
public class FeedbackGuidedStrategyTests
{
    [Test]
    public void FestTracksNovelTimelinesPerCampaign()
    {
        var timeline = new TimelineObserver();
        var firstPolicy = new FestPolicy();
        var secondPolicy = new FestPolicy();

        Assert.That(firstPolicy.Evaluate(timeline), Is.EqualTo(new SeedFeedback(20, 20)));
        Assert.That(firstPolicy.Evaluate(timeline), Is.Null);
        Assert.That(secondPolicy.Evaluate(timeline), Is.EqualTo(new SeedFeedback(20, 20)));
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
        var policy = new DuplicateTimelinePolicy();
        var strategy = new FeedbackGuidedStrategy(configuration, new ControlledRandom(configuration),
            new RandomScheduler(new ControlledRandom(configuration)), policy, policy);

        strategy.ObserveRunningResults(new TimelineObserver());
        Assert.That(strategy.TotalSavedInputs(), Is.Zero);

        strategy.ObserveRunningResults(new TimelineObserver());
        Assert.That(strategy.TotalSavedInputs(), Is.EqualTo(1));

        strategy.PrepareForNextIteration();
        Assert.That(policy.Parent?.Feedback, Is.SameAs(policy.DuplicateFeedback));
    }

    private sealed class DuplicateTimelinePolicy : IFeedbackPolicy, IRecordingMutationPolicy
    {
        private readonly TimelineDiversity _diversity = new();
        private readonly FestPolicy _fest = new();

        internal SeedFeedback DuplicateFeedback { get; } = new(10, 1);

        internal GeneratorRecord Parent { get; private set; }

        public SeedFeedback Evaluate(TimelineObserver timeline)
        {
            var (score, minHash) = _diversity.Measure(timeline);
            if (score != 0)
            {
                return null;
            }

            _diversity.RecordAdmission(minHash);
            return DuplicateFeedback;
        }

        public StrategyGenerator CreateFresh(StrategyGenerator current) => _fest.CreateFresh(current);

        public StrategyGenerator Mutate(GeneratorRecord parent)
        {
            Parent = parent;
            return _fest.Mutate(parent);
        }
    }
}
