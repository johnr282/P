using PChecker.Generator.Object;
using PChecker.SystematicTesting.Strategies.Probabilistic;

namespace PChecker.SystematicTesting.Strategies.Feedback;

internal record StrategyGenerator(ControlledRandom InputGenerator, IScheduler Scheduler);

internal record GeneratorRecord(SeedFeedback Feedback, StrategyGenerator Generator);

internal interface IRecordingMutationPolicy
{
    StrategyGenerator CreateFresh(StrategyGenerator current);

    StrategyGenerator Mutate(GeneratorRecord parent);
}
