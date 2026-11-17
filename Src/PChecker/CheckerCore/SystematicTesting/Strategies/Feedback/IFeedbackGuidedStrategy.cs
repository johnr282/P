using System.IO;
using PChecker.Feedback;
using PChecker.SystematicTesting.Traces;

namespace PChecker.SystematicTesting.Strategies.Feedback;

internal interface IFeedbackGuidedStrategy: ISchedulingStrategy
{
    public void ObserveRunningResults(TimelineObserver timelineObserver, ScheduleTrace scheduleTrace);
    public int TotalSavedInputs();
    public void DumpStats(TextWriter writer);
}
