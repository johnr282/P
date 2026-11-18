using System.IO;
using NUnit.Framework;
using PChecker.SystematicTesting.Strategies.Feedback.MonitorGuided;

namespace UnitTests;

[TestFixture]
public class MonitorGuidedStatisticsTests
{
    [Test]
    public void EmptyCampaignHasZeroRatesAndLengths()
    {
        var statistics = new MonitorGuidedStatistics();

        Assert.Multiple(() =>
        {
            Assert.That(statistics.ExecutionViolationProgressRate, Is.Zero);
            Assert.That(statistics.ExecutionsInvalidatingGuidance, Is.Zero);
            Assert.That(statistics.RootPrefixes, Is.Zero);
            Assert.That(statistics.SelectedPrefixViolationProgressRate, Is.Zero);
            Assert.That(statistics.SolverCacheHitRate, Is.Zero);
            Assert.That(statistics.MinSelectedPrefixLength, Is.Zero);
            Assert.That(statistics.MaxSelectedPrefixLength, Is.Zero);
            Assert.That(statistics.MeanSelectedPrefixLength, Is.Zero);
            Assert.That(statistics.MedianSelectedPrefixLength, Is.Zero);
            Assert.That(statistics.ChildPrefixProgressImprovementRate, Is.Zero);
            Assert.That(statistics.MinChildPrefixProgressImprovement, Is.Zero);
            Assert.That(statistics.MaxChildPrefixProgressImprovement, Is.Zero);
            Assert.That(statistics.MeanChildPrefixProgressImprovement, Is.Zero);
            Assert.That(statistics.MedianChildPrefixProgressImprovement, Is.Zero);
            Assert.That(statistics.TotalAdmittedPrefixes, Is.Zero);
            Assert.That(statistics.DiversityOnlyAdmissionRate, Is.Zero);
            Assert.That(statistics.ProgressOnlyAdmissionRate, Is.Zero);
            Assert.That(statistics.DiversityAndProgressAdmissionRate, Is.Zero);
        });
    }

    [Test]
    public void LengthDistributionWeightsRepeatedSelectionsAndIncludesEmptyPrefixes()
    {
        var statistics = new MonitorGuidedStatistics();
        statistics.RecordPrefixSelectedForMutation(8, true);
        Assert.That(statistics.MinSelectedPrefixLength, Is.EqualTo(8));
        Assert.That(statistics.MedianSelectedPrefixLength, Is.EqualTo(8));
        statistics.RecordPrefixSelectedForMutation(0, false);
        Assert.That(statistics.MedianSelectedPrefixLength, Is.EqualTo(4));
        statistics.RecordPrefixSelectedForMutation(2, true);
        statistics.RecordPrefixSelectedForMutation(2, true);

        Assert.Multiple(() =>
        {
            Assert.That(statistics.TotalPrefixesSelectedForMutation, Is.EqualTo(4));
            Assert.That(statistics.PrefixesWithViolationProgressSelectedForMutation, Is.EqualTo(3));
            Assert.That(statistics.SelectedPrefixViolationProgressRate, Is.EqualTo(0.75));
            Assert.That(statistics.MinSelectedPrefixLength, Is.Zero);
            Assert.That(statistics.MaxSelectedPrefixLength, Is.EqualTo(8));
            Assert.That(statistics.MeanSelectedPrefixLength, Is.EqualTo(3));
            Assert.That(statistics.MedianSelectedPrefixLength, Is.EqualTo(2));
        });

        statistics.RecordPrefixSelectedForMutation(10, true);
        Assert.That(statistics.MeanSelectedPrefixLength, Is.EqualTo(4.4));
        Assert.That(statistics.MedianSelectedPrefixLength, Is.EqualTo(2));
        Assert.That(statistics.MaxSelectedPrefixLength, Is.EqualTo(10));
    }

    [Test]
    public void LongPrefixesDoNotOverflowMeanOrEvenMedian()
    {
        var statistics = new MonitorGuidedStatistics();
        statistics.RecordPrefixSelectedForMutation(int.MaxValue, true);
        statistics.RecordPrefixSelectedForMutation(int.MaxValue - 1, true);

        Assert.That(statistics.MeanSelectedPrefixLength, Is.EqualTo(int.MaxValue - 0.5));
        Assert.That(statistics.MedianSelectedPrefixLength, Is.EqualTo(int.MaxValue - 0.5));
    }

    [Test]
    public void ChildProgressDistributionIncludesZerosAndRepeatedImprovements()
    {
        var statistics = new MonitorGuidedStatistics();
        statistics.RecordChildPrefixProgress(0);
        statistics.RecordChildPrefixProgress(8);
        Assert.That(statistics.MedianChildPrefixProgressImprovement, Is.EqualTo(4));
        statistics.RecordChildPrefixProgress(2);
        Assert.That(statistics.MedianChildPrefixProgressImprovement, Is.EqualTo(2));
        statistics.RecordChildPrefixProgress(8);
        Assert.That(statistics.MedianChildPrefixProgressImprovement, Is.EqualTo(5));
        statistics.RecordChildPrefixProgress(8);

        Assert.Multiple(() =>
        {
            Assert.That(statistics.TotalChildPrefixes, Is.EqualTo(5));
            Assert.That(statistics.ChildPrefixesWithProgressImprovement, Is.EqualTo(4));
            Assert.That(statistics.ChildPrefixProgressImprovementRate, Is.EqualTo(0.8));
            Assert.That(statistics.MinChildPrefixProgressImprovement, Is.Zero);
            Assert.That(statistics.MaxChildPrefixProgressImprovement, Is.EqualTo(8));
            Assert.That(statistics.MeanChildPrefixProgressImprovement, Is.EqualTo(5.2));
            Assert.That(statistics.MedianChildPrefixProgressImprovement, Is.EqualTo(8));
        });
    }

    [Test]
    public void AdmissionCategoriesAreExclusiveAndUseAllAdmissionsAsDenominator()
    {
        var statistics = new MonitorGuidedStatistics();
        statistics.RecordPrefixAdmission(true, false);
        statistics.RecordPrefixAdmission(false, true);
        statistics.RecordPrefixAdmission(true, true);
        statistics.RecordPrefixAdmission(true, false);

        Assert.Multiple(() =>
        {
            Assert.That(statistics.TotalAdmittedPrefixes, Is.EqualTo(4));
            Assert.That(statistics.PrefixesAdmittedForDiversityOnly, Is.EqualTo(2));
            Assert.That(statistics.PrefixesAdmittedForProgressOnly, Is.EqualTo(1));
            Assert.That(statistics.PrefixesAdmittedForDiversityAndProgress, Is.EqualTo(1));
            Assert.That(statistics.DiversityOnlyAdmissionRate, Is.EqualTo(0.5));
            Assert.That(statistics.ProgressOnlyAdmissionRate, Is.EqualTo(0.25));
            Assert.That(statistics.DiversityAndProgressAdmissionRate, Is.EqualTo(0.25));
        });
    }

    [Test]
    public void ReportIncludesRequestedMetrics()
    {
        var statistics = new MonitorGuidedStatistics();
        statistics.RecordExecution(true);
        statistics.RecordExecution(false, guidanceInvalidationStep: 12);
        statistics.RecordPrefixSelectedForMutation(4, true);
        statistics.RecordSolverCheck("formula", false);
        statistics.RecordSolverCheck("formula", true);
        statistics.RecordChildPrefixProgress(0);
        statistics.RecordChildPrefixProgress(1);
        statistics.RecordPrefixAdmission(true, false);
        statistics.RecordPrefixAdmission(false, true);
        statistics.RecordPrefixAdmission(true, true, isRoot: true);
        statistics.RecordPrefixAdmission(true, false);
        using var writer = new StringWriter();
        statistics.WriteTo(writer);
        var report = writer.ToString();

        Assert.That(report, Does.Contain("Executions with non-zero violation progress: 1/2 (50.00"));
        Assert.That(report, Does.Contain("Prefixes selected for mutation: 1; with non-zero violation progress: 1 (100.00"));
        Assert.That(report, Does.Contain("min=4, max=4, mean=4.00, median=4.00"));
        Assert.That(report, Does.Contain("hits=1, misses=1, hit rate=50.00"));
        Assert.That(report, Does.Contain("unique formulas=1"));
        Assert.That(report, Does.Contain("Child prefixes improving on their parent: 1/2 (50.00"));
        Assert.That(report, Does.Contain("min=0, max=1, mean=0.50, median=0.50"));
        Assert.That(report, Does.Contain("Admitted prefixes: 4; diversity only=2 (50.00"));
        Assert.That(report, Does.Contain("progress only=1 (25.00"));
        Assert.That(report, Does.Contain("both=1 (25.00"));
        Assert.That(report, Does.Contain("Executions invalidating guidance: 1"));
        Assert.That(report, Does.Contain("Guidance invalidation step: min=12, max=12, mean=12.00, median=12.00"));
        Assert.That(report, Does.Contain("Root prefixes: 1"));
    }
}
