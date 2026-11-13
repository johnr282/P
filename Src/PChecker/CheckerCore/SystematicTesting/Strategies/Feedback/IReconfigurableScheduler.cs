namespace PChecker.SystematicTesting.Strategies.Probabilistic;

internal interface IReconfigurableScheduler : IScheduler
{
    /// <summary>
    /// Resamples future scheduling decisions from the current execution boundary using the
    /// supplied seed, preserving the scheduler state established by the replayed prefix.
    /// </summary>
    void Reconfig(int seed);
}
