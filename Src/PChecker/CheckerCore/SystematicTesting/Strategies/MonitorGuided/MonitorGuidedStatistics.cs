using System;
using System.Collections.Generic;
using System.Linq;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    /// <summary>Statistics collected during one monitor-guided checker run.</summary>
    internal sealed class MonitorGuidedStatistics
    {
        private readonly HashSet<string> _solverFormulas = new(StringComparer.Ordinal);
        private readonly List<int> _choicesPerSchedulingCall = new();
        private readonly List<double> _progressCacheHitRatesPerSchedulingCall = new();
        private readonly List<int> _uniqueChoicesPerSchedulingCall = new();

        public long SolverCacheHits { get; private set; }
        public long SolverCacheMisses { get; private set; }
        public double SolverCacheHitRate => SolverCacheHits + SolverCacheMisses == 0
            ? 0
            : (double)SolverCacheHits / (SolverCacheHits + SolverCacheMisses);
        public long UniqueSolverFormulas => _solverFormulas.Count;
        public long InconsistentBehaviors { get; private set; }
        public long ConsistentBehaviors { get; private set; }
        public double BehaviorConsistencyRate => ConsistentBehaviors + InconsistentBehaviors == 0
            ? 0
            : (double)ConsistentBehaviors / (ConsistentBehaviors + InconsistentBehaviors);
        public long ComputeProgressPredictionRequests { get; private set; }
        public long KnownComputeProgressPredictions { get; private set; }
        public long PredictionsWithPositiveEffectProgress { get; private set; }
        public long ComputeProgressNullResults { get; private set; }
        public long PredictionsInvalidatingGuidance { get; private set; }
        public double KnownComputeProgressPredictionPercentage => ComputeProgressPredictionRequests == 0
            ? 0
            : (double)KnownComputeProgressPredictions / ComputeProgressPredictionRequests;
        public long FallbackChoices { get; private set; }
        public long TotalChoicesReturned { get; private set; }
        public long GuidanceInvalidations { get; private set; }
        public long CallsWithPositiveProgress { get; private set; }
        public long SchedulingChoiceCalls => _choicesPerSchedulingCall.Count;
        public double MeanChoicesPerSchedulingCall => _choicesPerSchedulingCall.Count == 0
            ? 0
            : _choicesPerSchedulingCall.Average();
        public double MedianChoicesPerSchedulingCall
        {
            get
            {
                if (_choicesPerSchedulingCall.Count == 0)
                    return 0;

                var sortedCounts = _choicesPerSchedulingCall.OrderBy(count => count).ToArray();
                int middle = sortedCounts.Length / 2;
                return sortedCounts.Length % 2 == 0
                    ? ((double)sortedCounts[middle - 1] + sortedCounts[middle]) / 2
                    : sortedCounts[middle];
            }
        }
        public int MinChoicesPerSchedulingCall => _choicesPerSchedulingCall.Count == 0
            ? 0
            : _choicesPerSchedulingCall.Min();
        public int MaxChoicesPerSchedulingCall => _choicesPerSchedulingCall.Count == 0
            ? 0
            : _choicesPerSchedulingCall.Max();
        public double MeanProgressCacheHitRatePerSchedulingCall =>
            _progressCacheHitRatesPerSchedulingCall.Count == 0
                ? 0
                : _progressCacheHitRatesPerSchedulingCall.Average();
        public double MedianProgressCacheHitRatePerSchedulingCall =>
            GetMedian(_progressCacheHitRatesPerSchedulingCall);
        public double MeanUniqueChoicesPerSchedulingCall =>
            _uniqueChoicesPerSchedulingCall.Count == 0
                ? 0
                : _uniqueChoicesPerSchedulingCall.Average();
        public double MedianUniqueChoicesPerSchedulingCall =>
            GetMedian(_uniqueChoicesPerSchedulingCall);

        public void RecordSolverCheck(string formulaKey, bool cacheHit)
        {
            _solverFormulas.Add(formulaKey);
            if (cacheHit) SolverCacheHits++;
            else SolverCacheMisses++;
        }

        public void RecordSchedulingChoiceMetrics(
            int choiceCount,
            int uniqueChoiceCount,
            int progressCacheHits)
        {
            _choicesPerSchedulingCall.Add(choiceCount);
            _uniqueChoicesPerSchedulingCall.Add(uniqueChoiceCount);
            _progressCacheHitRatesPerSchedulingCall.Add(choiceCount == 0
                ? 0
                : (double)progressCacheHits / choiceCount);
        }

        private static double GetMedian(IReadOnlyCollection<double> values)
        {
            if (values.Count == 0)
                return 0;

            var sortedValues = values.OrderBy(value => value).ToArray();
            int middle = sortedValues.Length / 2;
            return sortedValues.Length % 2 == 0
                ? (sortedValues[middle - 1] + sortedValues[middle]) / 2
                : sortedValues[middle];
        }

        private static double GetMedian(IReadOnlyCollection<int> values)
        {
            if (values.Count == 0)
                return 0;

            var sortedValues = values.OrderBy(value => value).ToArray();
            int middle = sortedValues.Length / 2;
            return sortedValues.Length % 2 == 0
                ? ((double)sortedValues[middle - 1] + sortedValues[middle]) / 2
                : sortedValues[middle];
        }

        public void RecordInconsistentBehavior() => InconsistentBehaviors++;
        public void RecordConsistentBehavior() => ConsistentBehaviors++;
        public void RecordComputeProgressPrediction(bool known)
        {
            ComputeProgressPredictionRequests++;
            if (known) KnownComputeProgressPredictions++;
        }
        public void RecordPositiveEffectProgressPrediction() => PredictionsWithPositiveEffectProgress++;
        public void RecordComputeProgressNullResult() => ComputeProgressNullResults++;
        public void RecordPredictionInvalidatingGuidance() => PredictionsInvalidatingGuidance++;
        public void RecordGuidanceInvalidation() => GuidanceInvalidations++;
        public void RecordPositiveProgressCall() => CallsWithPositiveProgress++;
        public void RecordChoiceReturned(bool usedFallback)
        {
            TotalChoicesReturned++;
            if (usedFallback) FallbackChoices++;
        }

    }
}
