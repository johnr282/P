using System.Collections.Generic;
using System.Linq;
using PChecker.Generator.Object;
using PChecker.IO.Debugging;
using PChecker.Random;
using PChecker.SystematicTesting.Operations;

namespace PChecker.SystematicTesting.Strategies.Probabilistic;

internal class POSScheduler: IScheduler
{
    private readonly IRandomValueGenerator _randomValueGenerator;

    /// <summary>
    /// List of prioritized operations.
    /// </summary>
    private readonly List<ulong> PrioritizedOperations = new();
    
    /// <summary>
    /// Initializes a new instance of the <see cref="POSScheduler"/> class.
    /// </summary>
    public POSScheduler(IRandomValueGenerator random)
    {
        _randomValueGenerator = random;
    }

    public bool GetNextSchedulingChoice(
        SchedulingChoice lastChoice, IEnumerable<SchedulingChoice> choices, out SchedulingChoice next)
    {
        var operations = choices.ToLookup(choice => choice.OperationId);
        var pendingSendTargets = operations.ToDictionary(
            operation => operation.Key,
            operation => (operation.First() as StateMachineSchedulingChoice)?.PendingSendTarget);

        if (PrioritizedOperations.Count == 0)
        {
            PrioritizedOperations.Add(lastChoice.OperationId);
        }

        foreach (var operation in operations.Where(operation => !PrioritizedOperations.Contains(operation.Key)))
        {
            var mIndex = _randomValueGenerator.Next(PrioritizedOperations.Count) + 1;
            PrioritizedOperations.Insert(mIndex, operation.Key);
            Debug.WriteLine("<POSLog> Detected new operation '{0}' at index '{1}'.", operation.Key, mIndex);
        }

        var enabledPriorities = PrioritizedOperations.Where(pendingSendTargets.ContainsKey).ToList();
        var operationId = enabledPriorities.FirstOrDefault(
            id => pendingSendTargets[id] is null, enabledPriorities[0]);
        var target = pendingSendTargets[operationId];
        if (target != null)
        {
            PrioritizedOperations.RemoveAll(id =>
                pendingSendTargets.TryGetValue(id, out var otherTarget) && target.Equals(otherTarget));
        }

        var operationChoices = operations[operationId].ToList();
        var choiceIndex = operationChoices.Count == 1 ? 0 : _randomValueGenerator.Next(operationChoices.Count);
        next = operationChoices[choiceIndex];
        return true;
    }

    public void Reset()
    {
        PrioritizedOperations.Clear();
    }

    /// <inheritdoc/>
    public virtual bool PrepareForNextIteration()
    {
        PrioritizedOperations.Clear();
        return true;
    }

    public IScheduler Mutate()
    {
        return new POSScheduler(((ControlledRandom)_randomValueGenerator).Mutate());
    }

    public IScheduler New()
    {
        return new POSScheduler(((ControlledRandom)_randomValueGenerator).New());
    }
}
