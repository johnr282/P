using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using PChecker.Configuration;
using PChecker.Feedback;
using PChecker.Generator.Object;
using PChecker.IO.Debugging;
using PChecker.Runtime.Exceptions;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Strategies.Feedback.MonitorGuided.SymbolicExecution.Solver;
using PChecker.SystematicTesting.Strategies.Probabilistic;
using PChecker.SystematicTesting.Traces;
using Plang.Compiler.TypeChecker.AST.Declarations;
using SeedRandom = System.Random;

namespace PChecker.SystematicTesting.Strategies.Feedback.MonitorGuided;

internal sealed class MonitorGuidedFeedbackStrategy : FeedbackGuidedStrategy
{
    private readonly Machine _monitorAST;
    private readonly MonitorAnalyzer _monitorAnalyzer;
    private readonly ISolver _solver;
    private readonly MonitorGuidance _initialGuidance;
    private MonitorGuidance _runGuidance;
    private readonly TimelineDiversity _diversity = new();
    private readonly SeedRandom _seedGenerator;
    private readonly MonitorGuidedFeedbackBoundaryMode _mutationBoundaryMode;
    private ReplayPlan _replayPlan;
    private ImmutableArray<RecordedStep> _expectedPrefix = ImmutableArray<RecordedStep>.Empty;
    private int _nextReconfiguration;
    private MonitorGuidedSeedFeedback _mutationParent;
    private long _nextRootPrefixId;
    private int? _guidanceInvalidationStep;

    internal MonitorGuidedStatistics Statistics { get; } = new();

    private record ExecutionPrefix(int Length, int DistanceToViolation);

    private readonly record struct RecordedStep(
        ScheduleStepType Type, ulong OperationId, int ChoiceId, bool? BooleanChoice, int? IntegerChoice);

    private readonly record struct Reconfiguration(int Position, int SchedulerSeed, int InputSeed);

    private sealed record ReplayPlan(int SchedulerSeed, int InputSeed, ImmutableArray<Reconfiguration> Reconfigurations);

    private sealed record MonitorGuidedSeedFeedback(
        double Priority, int MutationBudget, ImmutableArray<RecordedStep> ExecutionTrace, ReplayPlan ReplayPlan,
        int BestPrefixLength, int ViolationProgress, long RootPrefixId)
        : SeedFeedback(Priority, MutationBudget);

    private ExecutionPrefix _bestPrefix = null;
    private ExecutionPrefix _currentRunBestPrefix = null;

    internal MonitorGuidedFeedbackStrategy(
        CheckerConfiguration configuration,
        Machine monitorAst)
        : this(configuration, monitorAst,
            configuration.RandomGeneratorSeed is uint seed ? unchecked((int)seed) : SeedRandom.Shared.Next())
    {
    }

    private MonitorGuidedFeedbackStrategy(
        CheckerConfiguration configuration,
        Machine monitorAst,
        int seed)
        : base(configuration, CreateInputGenerator(seed), CreateScheduler(seed))
    {
        _seedGenerator = new SeedRandom(seed);
        _mutationBoundaryMode = configuration.MonitorGuidedFeedbackBoundary;
        _replayPlan = new ReplayPlan(seed, seed, ImmutableArray<Reconfiguration>.Empty);
        _monitorAST = monitorAst ?? throw new ArgumentNullException(nameof(monitorAst));
        _monitorAnalyzer = new MonitorAnalyzer(_monitorAST);
        switch (_monitorAST.Name)
        {
            case "OneLeaderPerElection":
                _monitorAnalyzer.GetLeaderMonitorGuidance(out _initialGuidance);
                break;
            case "Mutex":
                _monitorAnalyzer.GetMutexMonitorGuidance(out _initialGuidance);
                break;
            default:
                throw new PInternalException("Unsupported monitor");
        }

        if (_initialGuidance == null)
            throw new PInternalException("Failed to compute initial monitor guidance");

        _runGuidance = _initialGuidance.Clone();
        _solver = new Z3Solver(statistics: Statistics);
    }

    public override bool PrepareForNextIteration()
    {
        var hasNextIteration = base.PrepareForNextIteration();
        _runGuidance = _initialGuidance.Clone();
        _currentRunBestPrefix = null;
        _guidanceInvalidationStep = null;
        return hasNextIteration;
    }

    private int CurrentViolationProgress => _currentRunBestPrefix is null
        ? 0 : _initialGuidance.DistanceToViolation - _currentRunBestPrefix.DistanceToViolation;

    protected override void OnExecutionCompleted()
    {
        int progress = CurrentViolationProgress;
        Statistics.RecordExecution(progress > 0, guidanceInvalidationStep: _guidanceInvalidationStep);
        if (_mutationParent is not null)
        {
            Statistics.RecordChildPrefixProgress(Math.Max(0, progress - _mutationParent.ViolationProgress));
            Statistics.RecordRootPrefixExplored(_mutationParent.RootPrefixId);
        }
    }

    protected override void OnRecordingSelectedForMutation(GeneratorRecord parent)
    {
        var feedback = (MonitorGuidedSeedFeedback)parent.Feedback;
        _mutationParent = feedback;
        Statistics.RecordPrefixSelectedForMutation(feedback.BestPrefixLength, feedback.ViolationProgress > 0,
            _expectedPrefix.Length);
    }

    public override void DumpStats(TextWriter writer)
    {
        base.DumpStats(writer);
        Statistics.WriteTo(writer);
    }

    public override void NotifyEffects(
        SchedulingChoice lastChoice, IReadOnlyList<ExecutionEffect> effects, bool completeBehavior)
    {
        foreach (var effect in effects)
        {
            if (effect is not MonitorObservationEffect observation)
            {
                continue;
            }

            if (observation.TracePosition == 0)
            {
                throw new PInternalException(
                    $"Monitor '{observation.MonitorName}' observed an event before the first recorded trace step.");
            }

            if (string.Equals(observation.MonitorName, _monitorAST.Name, StringComparison.Ordinal))
            {
                UpdateCurrentRunBestPrefix(observation);
            }
        }
    }

    private void UpdateCurrentRunBestPrefix(MonitorObservationEffect observation)
    {
        if (_runGuidance is null || observation.Ignored)
        {
            return;
        }

        int previousDistance = _runGuidance.DistanceToViolation;
        bool wasGuidanceValid = _runGuidance.Valid;
        _runGuidance.Update(observation.ObservedEvent, _solver);
        if (wasGuidanceValid && !_runGuidance.Valid)
        {
            _guidanceInvalidationStep = observation.TracePosition;
        }

        if (_runGuidance.DistanceToViolation >= previousDistance ||
            !_runGuidance.Valid)
        {
            return;
        }

        int prefixLength = observation.TracePosition;
        int prefixViolationDistance = _runGuidance.DistanceToViolation;
        var currentPrefix = new ExecutionPrefix(prefixLength, prefixViolationDistance);

        if (ComparePrefixes(currentPrefix, _currentRunBestPrefix))
        {
            Debug.WriteLine(
                "<MonitorGuidedFeedbackStrategy> Updating _currentRunBestPrefix from {0} to {1} after event '{2}'.",
                (object)_currentRunBestPrefix ?? "null", currentPrefix, observation.ObservedEvent);
            _currentRunBestPrefix = currentPrefix;
        }
    }

    /// <summary>
    /// Returns true if a is better than b, false otherwise.
    /// </summary>
    private bool ComparePrefixes(ExecutionPrefix a, ExecutionPrefix b)
    {
        if (a == null) return false;
        if (b == null) return true;

        // We want the shortest prefix that gets closest to a violation.
        if (a.DistanceToViolation != b.DistanceToViolation)
        {
            return a.DistanceToViolation < b.DistanceToViolation;
        }

        return a.Length < b.Length;
    }

    protected override SeedFeedback ComputeFeedback(TimelineObserver timeline, ScheduleTrace scheduleTrace)
    {
        VerifyReplayPrefix(scheduleTrace);
        var (diversityScore, minHash) = _diversity.NormalizedMeasure(timeline);
        bool newBestPrefix = ComparePrefixes(_currentRunBestPrefix, _bestPrefix);

        if (diversityScore == 0 && !newBestPrefix)
            return null;

        // diversityScore is based on entire execution, but length and violationDistance
        // are based on only the best execution prefix; is this an issue?
        var prefix = _currentRunBestPrefix;
        var nullableResult = ComputePriorityAndBudget(diversityScore, prefix);
        if (nullableResult is not (double, int) result)
            return null;

        Debug.WriteLine(
            "<MonitorGuidedFeedbackStrategy> Computed feedback for prefix {0}: priority={1}, mutation budget={2}.",
            (object)prefix ?? "null", result.Priority, result.MutationBudget);

        // Save reconfigs necessary to reproduce this execution so mutation can 
        // preserve the prefix
        var executedPlan = _replayPlan with
        {
            Reconfigurations = _replayPlan.Reconfigurations.Slice(0, _nextReconfiguration)
        };

        var feedback = new MonitorGuidedSeedFeedback(
            result.Priority, result.MutationBudget, CopyExecutionTrace(scheduleTrace), executedPlan,
            prefix?.Length ?? 0, CurrentViolationProgress, _mutationParent?.RootPrefixId ?? _nextRootPrefixId++);

        if (newBestPrefix)
        {
            Debug.WriteLine(
                "<MonitorGuidedFeedbackStrategy> Updating _bestPrefix from {0} to {1}.",
                (object)_bestPrefix ?? "null", _currentRunBestPrefix);
            _bestPrefix = _currentRunBestPrefix;
        }

        _diversity.RecordAdmission(minHash);
        Statistics.RecordPrefixAdmission(diversityScore > 0, newBestPrefix, isRoot: _mutationParent is null);

        return feedback;
    }

    private static ImmutableArray<RecordedStep> CopyExecutionTrace(ScheduleTrace trace)
    {
        var steps = ImmutableArray.CreateBuilder<RecordedStep>(trace.Count);
        for (int i = 0; i < trace.Count; i++)
        {
            steps.Add(ReadStep(trace[i]));
        }

        return steps.MoveToImmutable();
    }

    private static RecordedStep ReadStep(ScheduleStep step) =>
        new(step.Type, step.ScheduledOperationId, step.ChoiceId, step.BooleanChoice, step.IntegerChoice);

    private void VerifyReplayPrefix(ScheduleTrace trace)
    {
        if (trace.Count < _expectedPrefix.Length)
        {
            throw new InvalidOperationException($"Schedule ended after {trace.Count} steps while replaying " +
                $"a monitor-guided prefix of {_expectedPrefix.Length} steps.");
        }

        for (int i = 0; i < _expectedPrefix.Length; i++)
        {
            if (ReadStep(trace[i]) != _expectedPrefix[i])
            {
                throw new InvalidOperationException($"Monitor-guided prefix replay diverged at choice {i}.");
            }
        }
    }

    private (double Priority, int MutationBudget)? ComputePriorityAndBudget(double diversityScore, ExecutionPrefix prefix)
    {
        // Null prefix is possible if execution has no monitor observations
        if (prefix == null)
            return (diversityScore, ComputeMutationBudget(diversityScore, 0.0));

        int length = prefix.Length;
        int violationDistance = prefix.DistanceToViolation;

        if (length == 0)
            throw new PInternalException("Recorded prefix of length 0");

        // If violationDistance == 0, this run should've encountered a bug. Not 
        // useful to discover same exact buggy execution multiple times, so discard
        // this execution.
        if (violationDistance == 0)
            return null;

        // Prefixes that consume entire step budget without reaching violation
        // are discarded.
        bool boundedSchedules = MaxScheduledSteps > 0;
        if (boundedSchedules && length >= MaxScheduledSteps)
            return null;

        // Prioritize high diversity, low length and violationDistance
        double progress = 1.0 - (double)violationDistance / _initialGuidance.DistanceToViolation;

        // JR TODO: Even with unbounded schedules, still want to prioritize shorter prefixes
        double remainingBudgetFraction = !boundedSchedules
            ? 1.0 : 1.0 - (double)length / MaxScheduledSteps;
        // Progress is more valuable than remaining budget; for example:
        // p1: 0.9 progress, 0.1 remaining budget
        // p2: 0.1 progress, 0.9 remaining budget
        // p1 should be prioritized over p2
        double monitorScore = (progress + progress * remainingBudgetFraction) / 2.0;
        double priority = diversityScore + monitorScore;

        return (priority, ComputeMutationBudget(diversityScore, monitorScore));
    }

    private int ComputeMutationBudget(double diversityScore, double monitorScore)
    {
        return (int)Math.Round(DefaultMutationBudget * (1.0 + diversityScore + monitorScore));
    }

    protected override StrategyGenerator MutateRecording(GeneratorRecord parent)
    {
        var feedback = (MonitorGuidedSeedFeedback)parent.Feedback;
        int boundary = ChooseMutationBoundary(feedback);

        // To preserve prefix defined by boundary, preserve parent reconfigs that
        // occurred before boundary. Append new reconfig at boundary to allow
        // exploration of new suffix.
        var reconfigurations = feedback.ReplayPlan.Reconfigurations
            .TakeWhile(change => change.Position < boundary)
            .Append(new Reconfiguration(boundary, _seedGenerator.Next(), _seedGenerator.Next()))
            .ToImmutableArray();
        return StartRecording(feedback.ReplayPlan with { Reconfigurations = reconfigurations },
            feedback.ExecutionTrace.Slice(0, boundary));
    }

    private int ChooseMutationBoundary(MonitorGuidedSeedFeedback feedback)
    {
        int executionLength = feedback.ExecutionTrace.Length;
        int boundary = _mutationBoundaryMode switch
        {
            MonitorGuidedFeedbackBoundaryMode.BestPrefix => feedback.BestPrefixLength,
            MonitorGuidedFeedbackBoundaryMode.RandomBestPrefix => SampleMutationBoundary(feedback.BestPrefixLength),
            MonitorGuidedFeedbackBoundaryMode.RandomExecution => SampleMutationBoundary(executionLength),
            _ => throw new ArgumentOutOfRangeException(nameof(_mutationBoundaryMode), _mutationBoundaryMode,
                "Unsupported monitor-guided feedback mutation boundary mode.")
        };

        Debug.WriteLine(
            "<MonitorGuidedFeedbackStrategy> Chose prefix for mutation: best prefix length={0}, " +
            "execution length={1}, preserved length={2}, priority={3}, mutation budget={4}.",
            feedback.BestPrefixLength, executionLength, boundary, feedback.Priority, feedback.MutationBudget);

        return boundary;
    }

    private int SampleMutationBoundary(int maxLength) => maxLength == 0 ? 0 : _seedGenerator.Next(maxLength + 1);

    protected override StrategyGenerator CreateFreshGenerator(StrategyGenerator current)
    {
        _mutationParent = null;
        return StartRecording(new ReplayPlan(_seedGenerator.Next(), _seedGenerator.Next(), ImmutableArray<Reconfiguration>.Empty),
            ImmutableArray<RecordedStep>.Empty);
    }

    protected override StrategyGenerator BeforeChoice(StrategyGenerator current)
    {
        var reconfigurations = _replayPlan.Reconfigurations;
        bool reconfigsComplete = _nextReconfiguration == reconfigurations.Length;
        if (reconfigsComplete ||
            reconfigurations[_nextReconfiguration].Position != ScheduledSteps)
        {
            return current;
        }

        var change = reconfigurations[_nextReconfiguration++];
        ((IReconfigurableScheduler)current.Scheduler).Reconfig(change.SchedulerSeed);
        return current with { InputGenerator = CreateInputGenerator(change.InputSeed) };
    }

    private StrategyGenerator StartRecording(ReplayPlan plan, ImmutableArray<RecordedStep> expectedPrefix)
    {
        _replayPlan = plan;
        _expectedPrefix = expectedPrefix;
        _nextReconfiguration = 0;
        return new StrategyGenerator(CreateInputGenerator(plan.InputSeed), CreateScheduler(plan.SchedulerSeed));
    }

    private static RandomScheduler CreateScheduler(int seed) => new(CreateInputGenerator(seed));

    private static ControlledRandom CreateInputGenerator(int seed) => new(new SeedRandom(seed));
}
