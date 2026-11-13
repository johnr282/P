using System;
using System.Collections.Generic;
using PChecker.Configuration;
using PChecker.Feedback;
using PChecker.Generator.Object;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Strategies.MonitorGuided;
using PChecker.SystematicTesting.Strategies.Probabilistic;
using Plang.Compiler.TypeChecker.AST.Declarations;

namespace PChecker.SystematicTesting.Strategies.Feedback;

/// <summary>
/// Computes initial guidance once for the selected monitor AST and tracks its observations per execution.
/// </summary>
internal abstract class MonitorGuidedFeedbackStrategy : FeedbackGuidedStrategy
{
    private readonly string _monitorName;
    private readonly MonitorGuidance _initialGuidance;
    private MonitorGuidance _runGuidance;

    protected MonitorGuidedFeedbackStrategy(
        CheckerConfiguration configuration,
        ControlledRandom inputGenerator,
        IScheduler scheduler,
        Machine monitorAst)
        : base(configuration, inputGenerator, scheduler)
    {
        ArgumentNullException.ThrowIfNull(monitorAst);
        _monitorName = monitorAst.Name;
        // JR TODO: Compute guidance
        _runGuidance = _initialGuidance?.Clone();
    }

    public sealed override bool PrepareForNextIteration()
    {
        var hasNextIteration = base.PrepareForNextIteration();
        _runGuidance = _initialGuidance?.Clone();
        ResetMonitorProgress();
        return hasNextIteration;
    }

    public sealed override void NotifyEffects(
        SchedulingChoice lastChoice, IReadOnlyList<ExecutionEffect> effects, bool completeBehavior)
    {
        foreach (var effect in effects)
        {
            if (effect is MonitorObservationEffect observation &&
                string.Equals(observation.MonitorName, _monitorName, StringComparison.Ordinal))
            {
                ObserveMonitorEvent(observation, _runGuidance);
            }
        }
    }

    protected sealed override SeedFeedback ComputeFeedback(TimelineObserver timeline) =>
        ComputeFeedback(timeline, _runGuidance);

    protected virtual void ResetMonitorProgress() { }

    protected abstract void ObserveMonitorEvent(MonitorObservationEffect observation, MonitorGuidance guidance);

    protected abstract SeedFeedback ComputeFeedback(TimelineObserver timeline, MonitorGuidance guidance);
}
