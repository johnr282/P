using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PChecker.SystematicTesting.Strategies.Feedback.MonitorGuided
{
    /// <summary>Statistics collected during one monitor-guided checker run.</summary>
    internal sealed class MonitorGuidedStatistics
    {
        private readonly HashSet<string> _solverFormulas = new(StringComparer.Ordinal);
        private readonly SortedDictionary<int, long> _selectedPrefixLengths = new();
        private readonly SortedDictionary<int, long> _childProgressImprovements = new();
        private readonly SortedDictionary<int, long> _guidanceInvalidationSteps = new();
        private long _selectedPrefixLengthSum;
        private long _childProgressImprovementSum;
        private long _guidanceInvalidationStepSum;

        public long SolverCacheHits { get; private set; }
        public long SolverCacheMisses { get; private set; }
        public double SolverCacheHitRate => SolverCacheHits + SolverCacheMisses == 0
            ? 0
            : (double)SolverCacheHits / (SolverCacheHits + SolverCacheMisses);
        public long UniqueSolverFormulas => _solverFormulas.Count;
        public long TotalExecutions { get; private set; }
        public long ExecutionsWithViolationProgress { get; private set; }
        public long ExecutionsInvalidatingGuidance { get; private set; }
        public int MinGuidanceInvalidationStep { get; private set; }
        public int MaxGuidanceInvalidationStep { get; private set; }
        public double MeanGuidanceInvalidationStep => ExecutionsInvalidatingGuidance == 0
            ? 0 : (double)_guidanceInvalidationStepSum / ExecutionsInvalidatingGuidance;
        public double MedianGuidanceInvalidationStep => GetMedian(_guidanceInvalidationSteps, ExecutionsInvalidatingGuidance);
        public double ExecutionViolationProgressRate => TotalExecutions == 0
            ? 0
            : (double)ExecutionsWithViolationProgress / TotalExecutions;
        public long TotalPrefixesSelectedForMutation { get; private set; }
        public long PrefixesWithViolationProgressSelectedForMutation { get; private set; }
        public double SelectedPrefixViolationProgressRate => TotalPrefixesSelectedForMutation == 0
            ? 0
            : (double)PrefixesWithViolationProgressSelectedForMutation / TotalPrefixesSelectedForMutation;
        public int MinSelectedPrefixLength { get; private set; }
        public int MaxSelectedPrefixLength { get; private set; }
        public double MeanSelectedPrefixLength => TotalPrefixesSelectedForMutation == 0
            ? 0
            : (double)_selectedPrefixLengthSum / TotalPrefixesSelectedForMutation;
        public double MedianSelectedPrefixLength => GetMedian(_selectedPrefixLengths, TotalPrefixesSelectedForMutation);

        public long TotalChildPrefixes { get; private set; }
        public long ChildPrefixesWithProgressImprovement { get; private set; }
        public double ChildPrefixProgressImprovementRate => TotalChildPrefixes == 0
            ? 0
            : (double)ChildPrefixesWithProgressImprovement / TotalChildPrefixes;
        public int MinChildPrefixProgressImprovement { get; private set; }
        public int MaxChildPrefixProgressImprovement { get; private set; }
        public double MeanChildPrefixProgressImprovement => TotalChildPrefixes == 0
            ? 0
            : (double)_childProgressImprovementSum / TotalChildPrefixes;
        public double MedianChildPrefixProgressImprovement => GetMedian(_childProgressImprovements, TotalChildPrefixes);

        public long PrefixesAdmittedForDiversityOnly { get; private set; }
        public long PrefixesAdmittedForProgressOnly { get; private set; }
        public long PrefixesAdmittedForDiversityAndProgress { get; private set; }
        public long RootPrefixes { get; private set; }
        public long TotalAdmittedPrefixes => PrefixesAdmittedForDiversityOnly +
            PrefixesAdmittedForProgressOnly + PrefixesAdmittedForDiversityAndProgress;
        public double DiversityOnlyAdmissionRate => TotalAdmittedPrefixes == 0
            ? 0 : (double)PrefixesAdmittedForDiversityOnly / TotalAdmittedPrefixes;
        public double ProgressOnlyAdmissionRate => TotalAdmittedPrefixes == 0
            ? 0 : (double)PrefixesAdmittedForProgressOnly / TotalAdmittedPrefixes;
        public double DiversityAndProgressAdmissionRate => TotalAdmittedPrefixes == 0
            ? 0 : (double)PrefixesAdmittedForDiversityAndProgress / TotalAdmittedPrefixes;

        private static double GetMedian(SortedDictionary<int, long> frequencies, long total)
        {
            if (total == 0)
                return 0;

            long lowerRank = (total - 1) / 2;
            long upperRank = total / 2;
            long count = 0;
            int lowerValue = 0;
            foreach (var (value, frequency) in frequencies)
            {
                if (count <= lowerRank && count + frequency > lowerRank)
                    lowerValue = value;

                count += frequency;
                if (count > upperRank)
                    return ((double)lowerValue + value) / 2;
            }

            return 0;
        }

        public void RecordSolverCheck(string formulaKey, bool cacheHit)
        {
            _solverFormulas.Add(formulaKey);
            if (cacheHit) SolverCacheHits++;
            else SolverCacheMisses++;
        }

        public void RecordExecution(bool hasViolationProgress, int? guidanceInvalidationStep = null)
        {
            if (guidanceInvalidationStep.HasValue)
                ArgumentOutOfRangeException.ThrowIfNegative(guidanceInvalidationStep.Value);

            TotalExecutions++;
            if (hasViolationProgress) ExecutionsWithViolationProgress++;
            if (guidanceInvalidationStep is int step)
            {
                MinGuidanceInvalidationStep = ExecutionsInvalidatingGuidance == 0
                    ? step : Math.Min(MinGuidanceInvalidationStep, step);
                MaxGuidanceInvalidationStep = Math.Max(MaxGuidanceInvalidationStep, step);
                _guidanceInvalidationStepSum += step;
                _guidanceInvalidationSteps.TryGetValue(step, out long frequency);
                _guidanceInvalidationSteps[step] = frequency + 1;
                ExecutionsInvalidatingGuidance++;
            }
        }

        public void RecordPrefixSelectedForMutation(int length, bool hasViolationProgress)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(length);

            MinSelectedPrefixLength = TotalPrefixesSelectedForMutation == 0
                ? length : Math.Min(MinSelectedPrefixLength, length);
            MaxSelectedPrefixLength = Math.Max(MaxSelectedPrefixLength, length);
            _selectedPrefixLengthSum += length;
            _selectedPrefixLengths.TryGetValue(length, out long frequency);
            _selectedPrefixLengths[length] = frequency + 1;
            TotalPrefixesSelectedForMutation++;
            if (hasViolationProgress) PrefixesWithViolationProgressSelectedForMutation++;
        }

        /// <summary>
        /// Records one completed child, including discarded children. Improvement is the
        /// reduction in violation distance beyond its parent; unchanged children contribute zero.
        /// </summary>
        public void RecordChildPrefixProgress(int improvement)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(improvement);

            MinChildPrefixProgressImprovement = TotalChildPrefixes == 0
                ? improvement : Math.Min(MinChildPrefixProgressImprovement, improvement);
            MaxChildPrefixProgressImprovement = Math.Max(MaxChildPrefixProgressImprovement, improvement);
            _childProgressImprovementSum += improvement;
            _childProgressImprovements.TryGetValue(improvement, out long frequency);
            _childProgressImprovements[improvement] = frequency + 1;
            TotalChildPrefixes++;
            if (improvement > 0) ChildPrefixesWithProgressImprovement++;
        }

        /// <summary>
        /// Classifies admitted prefixes using the existing diversity and best-prefix admission criteria.
        /// </summary>
        public void RecordPrefixAdmission(bool hasDiversity, bool improvesBestPrefix, bool isRoot = false)
        {
            if (hasDiversity && improvesBestPrefix) PrefixesAdmittedForDiversityAndProgress++;
            else if (hasDiversity) PrefixesAdmittedForDiversityOnly++;
            else if (improvesBestPrefix) PrefixesAdmittedForProgressOnly++;
            else throw new ArgumentException("An admitted prefix must provide diversity or improve the best prefix.");

            if (isRoot) RootPrefixes++;
        }

        public void WriteTo(TextWriter writer)
        {
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "..... Executions with non-zero violation progress: {0}/{1} ({2:P2})",
                ExecutionsWithViolationProgress, TotalExecutions, ExecutionViolationProgressRate));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "..... Executions invalidating guidance: {0}", ExecutionsInvalidatingGuidance));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "..... Guidance invalidation step: min={0}, max={1}, mean={2:F2}, median={3:F2}",
                MinGuidanceInvalidationStep, MaxGuidanceInvalidationStep,
                MeanGuidanceInvalidationStep, MedianGuidanceInvalidationStep));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "..... Prefixes selected for mutation: {0}; with non-zero violation progress: {1} ({2:P2})",
                TotalPrefixesSelectedForMutation, PrefixesWithViolationProgressSelectedForMutation,
                SelectedPrefixViolationProgressRate));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "..... Selected prefix length: min={0}, max={1}, mean={2:F2}, median={3:F2}",
                MinSelectedPrefixLength, MaxSelectedPrefixLength, MeanSelectedPrefixLength, MedianSelectedPrefixLength));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "..... Child prefixes improving on their parent: {0}/{1} ({2:P2})",
                ChildPrefixesWithProgressImprovement, TotalChildPrefixes, ChildPrefixProgressImprovementRate));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "..... Child prefix progress improvement (violation-distance reduction): min={0}, max={1}, mean={2:F2}, median={3:F2}",
                MinChildPrefixProgressImprovement, MaxChildPrefixProgressImprovement,
                MeanChildPrefixProgressImprovement, MedianChildPrefixProgressImprovement));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "..... Admitted prefixes: {0}; diversity only={1} ({2:P2}), progress only={3} ({4:P2}), both={5} ({6:P2})",
                TotalAdmittedPrefixes, PrefixesAdmittedForDiversityOnly, DiversityOnlyAdmissionRate,
                PrefixesAdmittedForProgressOnly, ProgressOnlyAdmissionRate,
                PrefixesAdmittedForDiversityAndProgress, DiversityAndProgressAdmissionRate));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "..... Root prefixes: {0}", RootPrefixes));
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "..... Solver cache: hits={0}, misses={1}, hit rate={2:P2}, unique formulas={3}",
                SolverCacheHits, SolverCacheMisses, SolverCacheHitRate, UniqueSolverFormulas));
        }

    }
}
