using System.Collections.Generic;
using PChecker.SystematicTesting.Operations;

namespace PChecker.SystematicTesting.Strategies.Probabilistic;

internal interface IScheduler
{
    public bool GetNextSchedulingChoice(
        SchedulingChoice lastChoice, IEnumerable<SchedulingChoice> choices, out SchedulingChoice next);
    public void Reset();
    public bool PrepareForNextIteration();
    public IScheduler Mutate();
    public IScheduler New();
}
