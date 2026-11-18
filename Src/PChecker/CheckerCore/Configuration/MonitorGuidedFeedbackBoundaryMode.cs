namespace PChecker.Configuration;

/// <summary>
/// Controls how much of a saved execution monitor-guided feedback preserves before mutation.
/// </summary>
public enum MonitorGuidedFeedbackBoundaryMode
{
    RandomExecution = 0,
    BestPrefix,
    RandomBestPrefix
}
