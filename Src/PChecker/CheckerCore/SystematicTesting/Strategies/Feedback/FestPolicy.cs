using PChecker.Feedback;

namespace PChecker.SystematicTesting.Strategies.Feedback;

internal sealed class FestPolicy : IFeedbackPolicy, IRecordingMutationPolicy
{
    private readonly TimelineDiversity _diversity = new();

    public SeedFeedback Evaluate(TimelineObserver timeline)
    {
        var (score, minHash) = _diversity.Measure(timeline);
        if (score == 0)
        {
            return null;
        }

        _diversity.RecordAdmission(minHash);
        return new SeedFeedback(score, score);
    }

    public StrategyGenerator CreateFresh(StrategyGenerator current) =>
        new(current.InputGenerator.New(), current.Scheduler.New());

    public StrategyGenerator Mutate(GeneratorRecord parent) =>
        new(parent.Generator.InputGenerator.Mutate(), parent.Generator.Scheduler.Mutate());
}
