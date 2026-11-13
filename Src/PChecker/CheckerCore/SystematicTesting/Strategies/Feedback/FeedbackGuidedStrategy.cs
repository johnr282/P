using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PChecker.Configuration;
using PChecker.Feedback;
using PChecker.Generator.Object;
using PChecker.SystematicTesting.Strategies.Probabilistic;
using PChecker.SystematicTesting.Operations;

namespace PChecker.SystematicTesting.Strategies.Feedback;


internal record SeedFeedback(int Priority, int MutationBudget);

internal record StrategyGenerator(ControlledRandom InputGenerator, IScheduler Scheduler);

internal record GeneratorRecord(SeedFeedback Feedback, StrategyGenerator Generator);

internal abstract class FeedbackGuidedStrategy : IFeedbackGuidedStrategy
{
    private readonly int _maxScheduledSteps;
    private readonly List<GeneratorRecord> _savedGenerators = new();
    private readonly HashSet<GeneratorRecord> _visitedGenerators = new();
    private readonly System.Random _rnd = new();

    private StrategyGenerator _generator;
    private int _scheduledSteps;
    private int _pendingMutations;
    private bool _shouldExploreNew;
    private GeneratorRecord _currentParent;

    protected FeedbackGuidedStrategy(
        CheckerConfiguration checkerConfiguration,
        ControlledRandom inputGenerator,
        IScheduler scheduler)
    {
        _maxScheduledSteps = checkerConfiguration.MaxUnfairSchedulingSteps;
        _generator = new StrategyGenerator(inputGenerator, scheduler);
    }

    /// <inheritdoc/>
    public bool GetNextSchedulingChoice(
        SchedulingChoice lastChoice, IEnumerable<SchedulingChoice> choices, out SchedulingChoice next)
    {
        var result = _generator.Scheduler.GetNextSchedulingChoice(lastChoice, choices, out next);
        _scheduledSteps++;
        return result;
    }

    /// <inheritdoc/>
    public bool GetNextBooleanChoice(SchedulingChoice lastChoice, int maxValue, out bool next)
    {
        next = _generator.InputGenerator.Next(maxValue) == 0;
        _scheduledSteps++;
        return true;
    }

    /// <inheritdoc/>
    public bool GetNextIntegerChoice(SchedulingChoice lastChoice, int maxValue, out int next)
    {
        next = _generator.InputGenerator.Next(maxValue);
        _scheduledSteps++;
        return true;
    }

    /// <inheritdoc/>
    public virtual bool PrepareForNextIteration()
    {
        _scheduledSteps = 0;
        PrepareNextInput();
        return true;
    }

    /// <inheritdoc/>
    public int GetScheduledSteps()
    {
        return _scheduledSteps;
    }

    /// <inheritdoc/>
    public bool HasReachedMaxSchedulingSteps()
    {
        if (_maxScheduledSteps == 0)
        {
            return false;
        }

        return _scheduledSteps >= _maxScheduledSteps;
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
        _scheduledSteps = 0;
    }

    /// <inheritdoc/>
    public virtual void NotifyEffects(
        SchedulingChoice lastChoice, IReadOnlyList<ExecutionEffect> effects, bool completeBehavior)
    {
    }

    public void ObserveRunningResults(TimelineObserver timelineObserver)
    {
        var feedback = ComputeFeedback(timelineObserver);
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
            _pendingMutations = 50;
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
                    _pendingMutations = 50;
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
    /// </summary>
    protected abstract SeedFeedback ComputeFeedback(TimelineObserver timeline);

    protected abstract StrategyGenerator MutateRecording(GeneratorRecord parent);

    protected virtual StrategyGenerator CreateFreshGenerator(StrategyGenerator current) =>
        new(current.InputGenerator.New(), current.Scheduler.New());

    public void DumpStats(TextWriter writer)
    {
        writer.WriteLine($"..... Total saved: {TotalSavedInputs()}, pending mutations: {_pendingMutations}, visited generators: {_visitedGenerators.Count}");
    }
}
