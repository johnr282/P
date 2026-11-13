using PChecker.Configuration;
using PChecker.Feedback;
using PChecker.Generator.Object;
using PChecker.SystematicTesting.Strategies.Probabilistic;

namespace PChecker.SystematicTesting.Strategies.Feedback;

internal sealed class FestStrategy : FeedbackGuidedStrategy
{
    private readonly TimelineDiversity _diversity = new();

    internal FestStrategy(CheckerConfiguration configuration, ControlledRandom inputGenerator, IScheduler scheduler)
        : base(configuration, inputGenerator, scheduler)
    {
    }

    protected override SeedFeedback ComputeFeedback(TimelineObserver timeline)
    {
        var (score, minHash) = _diversity.Measure(timeline);
        if (score == 0)
        {
            return null;
        }

        _diversity.RecordAdmission(minHash);
        return new SeedFeedback(score, score);
    }

    protected override StrategyGenerator MutateRecording(GeneratorRecord parent) =>
        new(parent.Generator.InputGenerator.Mutate(), parent.Generator.Scheduler.Mutate());
}
