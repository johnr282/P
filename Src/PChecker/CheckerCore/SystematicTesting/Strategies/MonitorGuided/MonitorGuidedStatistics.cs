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
        public bool PredictionComparisonEnabled { get; private set; }
        public long PredictionComparisonChoices { get; private set; }
        public long PredictionChangedProgressScores { get; private set; }
        public long PredictionInducedNullProgressResults { get; private set; }
        public long DecisionsWithDifferentBestChoiceSets { get; private set; }
        public long SelectedChoicesNotBestWithoutPredictions { get; private set; }
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

        public void RecordSolverCheck(string formulaKey, bool cacheHit)
        {
            _solverFormulas.Add(formulaKey);
            if (cacheHit) SolverCacheHits++;
            else SolverCacheMisses++;
        }

        public void RecordSchedulingChoiceCount(int choiceCount) =>
            _choicesPerSchedulingCall.Add(choiceCount);

        public void SetPredictionComparisonEnabled(bool enabled) => PredictionComparisonEnabled = enabled;

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

        public void RecordPredictionComparison(
            IReadOnlyList<(int? WithPredictions, int? WithoutPredictions)> comparisons,
            bool bestChoiceSetChanged)
        {
            foreach (var (withPredictions, withoutPredictions) in comparisons)
            {
                PredictionComparisonChoices++;
                if (withPredictions != withoutPredictions)
                    PredictionChangedProgressScores++;
                if (withPredictions == null && withoutPredictions != null)
                    PredictionInducedNullProgressResults++;
            }

            if (bestChoiceSetChanged)
                DecisionsWithDifferentBestChoiceSets++;
        }

        public void RecordSelectedChoiceComparison(bool selectedChoiceIsCounterfactualBest)
        {
            if (!selectedChoiceIsCounterfactualBest)
                SelectedChoicesNotBestWithoutPredictions++;
        }
    }
}
