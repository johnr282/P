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
/// Receives execution observations for the monitor selected from the P test.
/// </summary>
internal class MonitorGuidedFeedbackStrategy : FeedbackGuidedStrategy
{
    internal Machine MonitorAst { get; }

    private readonly MonitorGuidance _initialGuidance;
    private MonitorGuidance _runGuidance;

    internal MonitorGuidedFeedbackStrategy(
        CheckerConfiguration configuration,
        ControlledRandom inputGenerator,
        IScheduler scheduler,
        Machine monitorAst)
        : base(configuration, inputGenerator, scheduler)
    {
        MonitorAst = monitorAst ?? throw new ArgumentNullException(nameof(monitorAst));
        // TODO: Compute initial guidance from the AST before the campaign begins.
        _initialGuidance = null;
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
                string.Equals(observation.MonitorName, MonitorAst.Name, StringComparison.Ordinal))
            {
                ObserveMonitorEvent(observation, _runGuidance);
            }
        }
    }

    protected sealed override SeedFeedback ComputeFeedback(TimelineObserver timeline) =>
        ComputeFeedback(timeline, _runGuidance);

    protected virtual void ResetMonitorProgress() { }

    protected virtual void ObserveMonitorEvent(MonitorObservationEffect observation, MonitorGuidance guidance) { }

    protected virtual SeedFeedback ComputeFeedback(TimelineObserver timeline, MonitorGuidance guidance) => null;

    protected override StrategyGenerator MutateRecording(GeneratorRecord parent) =>
        throw new NotSupportedException("Monitor-guided recording mutation is not implemented.");
}
