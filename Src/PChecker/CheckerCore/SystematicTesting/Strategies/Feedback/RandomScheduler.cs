using System.Collections.Generic;
using System.Linq;
using PChecker.Generator.Object;
using PChecker.Random;
using PChecker.SystematicTesting.Operations;

namespace PChecker.SystematicTesting.Strategies.Probabilistic;

internal class RandomScheduler : IScheduler
{
    private readonly IRandomValueGenerator _randomValueGenerator;
    public RandomScheduler(IRandomValueGenerator randomValueGenerator)
    {
        _randomValueGenerator = randomValueGenerator;
    }

    public bool GetNextSchedulingChoice(
        SchedulingChoice lastChoice, IEnumerable<SchedulingChoice> choices, out SchedulingChoice next)
    {
        var choicesList = choices.ToList();
        var choiceIndex = _randomValueGenerator.Next(choicesList.Count);
        next = choicesList[choiceIndex];
        return true;
    }

    public void Reset()
    {
    }

    public bool PrepareForNextIteration()
    {
        return true;
    }

    IScheduler IScheduler.Mutate()
    {
        return new RandomScheduler(((ControlledRandom)_randomValueGenerator).Mutate());
    }

    IScheduler IScheduler.New()
    {
        return new RandomScheduler(((ControlledRandom)_randomValueGenerator).New());
    }
}
