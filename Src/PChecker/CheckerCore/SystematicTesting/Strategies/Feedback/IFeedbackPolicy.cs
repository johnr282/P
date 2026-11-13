using PChecker.Feedback;

namespace PChecker.SystematicTesting.Strategies.Feedback;

internal record SeedFeedback(int Priority, int MutationBudget);

internal interface IFeedbackPolicy
{
    /// <summary>
    /// Evaluates every completed run.
    /// Returns null to discard the run or feedback with a positive mutation budget to admit it.
    /// </summary>
    SeedFeedback Evaluate(TimelineObserver timeline);
}
