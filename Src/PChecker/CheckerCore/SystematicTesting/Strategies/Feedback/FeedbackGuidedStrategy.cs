using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PChecker.Configuration;
using PChecker.Feedback;
using PChecker.Generator.Object;
using PChecker.SystematicTesting.Strategies.Probabilistic;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Traces;

namespace PChecker.SystematicTesting.Strategies.Feedback;


internal record SeedFeedback(double Priority, int MutationBudget);

internal record StrategyGenerator(ControlledRandom InputGenerator, IScheduler Scheduler);

internal record GeneratorRecord(SeedFeedback Feedback, StrategyGenerator Generator);

internal abstract class FeedbackGuidedStrategy : IFeedbackGuidedStrategy
{
    private readonly List<GeneratorRecord> _savedGenerators = new();
    private readonly HashSet<GeneratorRecord> _visitedGenerators = new();
    private readonly System.Random _rnd = new();

    private StrategyGenerator _generator;
    protected readonly int MaxScheduledSteps;
    protected int ScheduledSteps { get; private set; }
    protected const int DefaultMutationBudget = 50;
    private int _pendingMutations;
    private bool _shouldExploreNew;
    private GeneratorRecord _currentParent;

    protected FeedbackGuidedStrategy(
        CheckerConfiguration checkerConfiguration,
        ControlledRandom inputGenerator,
        IScheduler scheduler)
    {
        MaxScheduledSteps = checkerConfiguration.MaxUnfairSchedulingSteps;
        _generator = new StrategyGenerator(inputGenerator, scheduler);
    }

    /// <inheritdoc/>
    public bool GetNextSchedulingChoice(
        SchedulingChoice lastChoice, IEnumerable<SchedulingChoice> choices, out SchedulingChoice next)
    {
        _generator = BeforeChoice(_generator);
        var result = _generator.Scheduler.GetNextSchedulingChoice(lastChoice, choices, out next);
        ScheduledSteps++;
        return result;
    }

    /// <inheritdoc/>
    public bool GetNextBooleanChoice(SchedulingChoice lastChoice, int maxValue, out bool next)
    {
        _generator = BeforeChoice(_generator);
        next = _generator.InputGenerator.Next(maxValue) == 0;
        ScheduledSteps++;
        return true;
    }

    /// <inheritdoc/>
    public bool GetNextIntegerChoice(SchedulingChoice lastChoice, int maxValue, out int next)
    {
        _generator = BeforeChoice(_generator);
        next = _generator.InputGenerator.Next(maxValue);
        ScheduledSteps++;
        return true;
    }

    /// <inheritdoc/>
    public virtual bool PrepareForNextIteration()
    {
        ScheduledSteps = 0;
        PrepareNextInput();
        return true;
    }

    /// <inheritdoc/>
    public int GetScheduledSteps()
    {
        return ScheduledSteps;
    }

    /// <inheritdoc/>
    public bool HasReachedMaxSchedulingSteps()
    {
        if (MaxScheduledSteps == 0)
        {
            return false;
        }

        return ScheduledSteps >= MaxScheduledSteps;
    }

    /// <inheritdoc/>
    public bool IsFair()
    {
        return true;
    }

    /// <inheritdoc/>
    public string GetDescription()
    {
        return "feedback";
    }

    /// <inheritdoc/>
    public void Reset()
    {
        ScheduledSteps = 0;
    }

    /// <inheritdoc/>
    public virtual void NotifyEffects(
        SchedulingChoice lastChoice, IReadOnlyList<ExecutionEffect> effects, bool completeBehavior)
    {
    }

    public void ObserveRunningResults(TimelineObserver timelineObserver, ScheduleTrace scheduleTrace)
    {
        var feedback = ComputeFeedback(timelineObserver, scheduleTrace);
        if (feedback is null)
        {
            return;
        }

        if (feedback.MutationBudget <= 0)
        {
            throw new InvalidOperationException("An admitted recording must have a positive mutation budget.");
        }

        var index = 0;
        while (index < _savedGenerators.Count && feedback.Priority < _savedGenerators[index].Feedback.Priority)
        {
            index++;
        }

        _savedGenerators.Insert(index, new GeneratorRecord(feedback, _generator));
    }

    public int TotalSavedInputs()
    {
        return _savedGenerators.Count;
    }

    private void PrepareNextInput()
    {
        _generator.Scheduler.PrepareForNextIteration();
        if (_savedGenerators.Count == 0)
        {
            _generator = CreateFreshGenerator(_generator);
            return;
        }

        if (_currentParent == null && !_shouldExploreNew)
        {
            _currentParent = _savedGenerators.First();
            _visitedGenerators.Add(_currentParent);
            _pendingMutations = _currentParent.Feedback.MutationBudget;
        }

        if (_pendingMutations == 0)
        {
            _shouldExploreNew = false;
            bool found = false;
            foreach (var generator in _savedGenerators)
            {
                if (_visitedGenerators.Contains(generator)) continue;
                _currentParent = generator;
                _visitedGenerators.Add(generator);
                _pendingMutations = generator.Feedback.MutationBudget;
                found = true;
                break;
            }

            if (!found)
            {
                if (_rnd.NextDouble() < 0.5)
                {
                    _visitedGenerators.Clear();
                    _currentParent = _savedGenerators.First();
                    _visitedGenerators.Add(_currentParent);
                    _pendingMutations = _currentParent.Feedback.MutationBudget;
                }
                else
                {
                    _shouldExploreNew = true;
                    _currentParent = null;
                    _pendingMutations = DefaultMutationBudget;
                }
            }
        }

        _generator = _shouldExploreNew
            ? CreateFreshGenerator(_generator)
            : MutateRecording(_currentParent);
        _pendingMutations -= 1;
    }

    /// <summary>
    /// Returns feedback for an admitted recording, or null to discard it.
    /// The schedule trace belongs to the completed run. Copy any steps needed after this call.
    /// </summary>
    protected abstract SeedFeedback ComputeFeedback(TimelineObserver timeline, ScheduleTrace scheduleTrace);

    protected abstract StrategyGenerator MutateRecording(GeneratorRecord parent);

    /// <summary>
    /// Runs at the current choice position before either random stream is used.
    /// </summary>
    protected virtual StrategyGenerator BeforeChoice(StrategyGenerator current) => current;

    protected virtual StrategyGenerator CreateFreshGenerator(StrategyGenerator current) =>
        new(current.InputGenerator.New(), current.Scheduler.New());

    public void DumpStats(TextWriter writer)
    {
        writer.WriteLine($"..... Total saved: {TotalSavedInputs()}, pending mutations: {_pendingMutations}, visited generators: {_visitedGenerators.Count}");
    }
}
