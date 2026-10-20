namespace PChecker.Configuration;

/// <summary>
/// Predictor types for the monitor-guided scheduling strategy.
/// </summary>
public enum MonitorGuidedPredictorType
{
    /// <summary>
    /// Predicts effects by matching exact previously observed behavior.
    /// </summary>
    Exact,

    /// <summary>
    /// Does not predict effects.
    /// </summary>
    Null
}
