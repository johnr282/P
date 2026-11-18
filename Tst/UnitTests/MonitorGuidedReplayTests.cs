using System;
using System.Linq;
using NUnit.Framework;
using PChecker.Configuration;
using PChecker.Feedback;
using PChecker.Runtime.Values;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Strategies.Feedback.MonitorGuided;
using PChecker.SystematicTesting.Traces;
using Plang.Compiler;
using Plang.Compiler.TypeChecker.AST.Declarations;
using Plang.Compiler.TypeChecker.AST.States;
using Plang.Compiler.TypeChecker.Types;
using RuntimeEvent = PChecker.Runtime.Events.Event;

namespace UnitTests;

[TestFixture]
public class MonitorGuidedReplayTests
{
    [Test]
    public void ChildReconfiguresAfterItsPrefixAndGrandchildReplaysTheEarlierReconfiguration()
    {
        const int seed = 17;
        // The initial seed's priority is 20/70, so its mutation budget rounds to 64.
        const int initialMutationBudget = 64;
        var configuration = CheckerConfiguration.Create().WithRandomGeneratorSeed(seed);
        var strategy = new MonitorGuidedFeedbackStrategy(configuration, CreateMonitorAst());
        var timeline = new TimelineObserver();
        // An empty original prefix lets the child make progress without reaching a violation.
        var original = Run(strategy, observeLeader: false);
        strategy.ObserveRunningResults(timeline, original);

        strategy.PrepareForNextIteration();
        var child = Run(strategy, observeLeader: true);
        var seeds = new Random(seed);
        var schedulerRandom = new Random(seeds.Next());
        var inputRandom = new Random(seeds.Next());
        Assert.That(child[0].ScheduledOperationId, Is.EqualTo((ulong)(schedulerRandom.Next() % 3)));
        Assert.That(child[1].BooleanChoice, Is.EqualTo(inputRandom.Next() % 2 == 0));
        Assert.That(child[2].IntegerChoice, Is.EqualTo(inputRandom.Next()));
        Assert.That(child[3].IntegerChoice, Is.EqualTo(inputRandom.Next()));
        Assert.That(child[4].ScheduledOperationId, Is.EqualTo((ulong)(schedulerRandom.Next() % 3)));
        strategy.ObserveRunningResults(timeline, child);
        Assert.That(strategy.TotalSavedInputs(), Is.EqualTo(2));

        // Exhaust the initial parent's batch so the child becomes the next parent.
        for (int i = 1; i < initialMutationBudget; i++)
        {
            strategy.PrepareForNextIteration();
            strategy.ObserveRunningResults(timeline, Run(strategy, observeLeader: false));
            seeds.Next();
            seeds.Next();
        }

        strategy.PrepareForNextIteration();
        var grandchild = Run(strategy, observeLeader: true);
        Assert.That(grandchild.Take(5).Select(Choice), Is.EqualTo(child.Take(5).Select(Choice)));
        seeds.Next();
        Assert.That(grandchild[5].IntegerChoice, Is.EqualTo(new Random(seeds.Next()).Next()));
        Assert.DoesNotThrow(() => strategy.ObserveRunningResults(timeline, grandchild));

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Statistics.TotalExecutions, Is.EqualTo(initialMutationBudget + 2));
            Assert.That(strategy.Statistics.ExecutionsWithViolationProgress, Is.EqualTo(2));
            Assert.That(strategy.Statistics.ExecutionViolationProgressRate,
                Is.EqualTo(2.0 / (initialMutationBudget + 2)));
            Assert.That(strategy.Statistics.TotalPrefixesSelectedForMutation, Is.EqualTo(initialMutationBudget + 1));
            Assert.That(strategy.Statistics.PrefixesWithViolationProgressSelectedForMutation, Is.EqualTo(1));
            Assert.That(strategy.Statistics.SelectedPrefixViolationProgressRate,
                Is.EqualTo(1.0 / (initialMutationBudget + 1)));
            Assert.That(strategy.Statistics.MinSelectedPrefixLength, Is.Zero);
            Assert.That(strategy.Statistics.MaxSelectedPrefixLength, Is.EqualTo(5));
            Assert.That(strategy.Statistics.MeanSelectedPrefixLength, Is.EqualTo(5.0 / (initialMutationBudget + 1)));
            Assert.That(strategy.Statistics.MedianSelectedPrefixLength, Is.Zero);
            Assert.That(strategy.Statistics.SolverCacheMisses, Is.EqualTo(1));
            Assert.That(strategy.Statistics.SolverCacheHits, Is.EqualTo(1));
            Assert.That(strategy.Statistics.UniqueSolverFormulas, Is.EqualTo(1));
            Assert.That(strategy.Statistics.TotalChildPrefixes, Is.EqualTo(initialMutationBudget + 1));
            Assert.That(strategy.Statistics.ChildPrefixesWithProgressImprovement, Is.EqualTo(1));
            Assert.That(strategy.Statistics.ChildPrefixProgressImprovementRate,
                Is.EqualTo(1.0 / (initialMutationBudget + 1)));
            Assert.That(strategy.Statistics.MinChildPrefixProgressImprovement, Is.Zero);
            Assert.That(strategy.Statistics.MaxChildPrefixProgressImprovement, Is.EqualTo(1));
            Assert.That(strategy.Statistics.MeanChildPrefixProgressImprovement,
                Is.EqualTo(1.0 / (initialMutationBudget + 1)));
            Assert.That(strategy.Statistics.MedianChildPrefixProgressImprovement, Is.Zero);
            Assert.That(strategy.Statistics.TotalAdmittedPrefixes, Is.EqualTo(strategy.TotalSavedInputs()));
            Assert.That(strategy.Statistics.PrefixesAdmittedForDiversityOnly, Is.EqualTo(1));
            Assert.That(strategy.Statistics.PrefixesAdmittedForProgressOnly, Is.EqualTo(1));
            Assert.That(strategy.Statistics.PrefixesAdmittedForDiversityAndProgress, Is.Zero);
            Assert.That(strategy.Statistics.RootPrefixes, Is.EqualTo(1));
            Assert.That(strategy.Statistics.ExecutionsInvalidatingGuidance, Is.Zero);
        });
    }

    [Test]
    public void DiscardedViolatingExecutionStillCountsAsProgress()
    {
        var configuration = CheckerConfiguration.Create().WithRandomGeneratorSeed(17);
        var strategy = new MonitorGuidedFeedbackStrategy(configuration, CreateMonitorAst());
        var trace = Run(strategy, observeLeader: true);
        Observe(strategy, new RunTaskChoice(0), trace.Count, 8);
        strategy.ObserveRunningResults(new TimelineObserver(), trace);
        strategy.PrepareForNextIteration();

        Assert.Multiple(() =>
        {
            Assert.That(strategy.TotalSavedInputs(), Is.Zero);
            Assert.That(strategy.Statistics.TotalExecutions, Is.EqualTo(1));
            Assert.That(strategy.Statistics.ExecutionsWithViolationProgress, Is.EqualTo(1));
            Assert.That(strategy.Statistics.ExecutionViolationProgressRate, Is.EqualTo(1));
            Assert.That(strategy.Statistics.TotalPrefixesSelectedForMutation, Is.Zero);
            Assert.That(strategy.Statistics.SolverCacheMisses, Is.EqualTo(2));
            Assert.That(strategy.Statistics.UniqueSolverFormulas, Is.EqualTo(2));
            Assert.That(strategy.Statistics.TotalChildPrefixes, Is.Zero);
            Assert.That(strategy.Statistics.TotalAdmittedPrefixes, Is.Zero);
            Assert.That(strategy.Statistics.RootPrefixes, Is.Zero);
            Assert.That(strategy.Statistics.ExecutionsInvalidatingGuidance, Is.Zero);
        });
    }

    [Test]
    public void PrefixAtStepBoundStillCountsAsExecutionProgressWhenDiscarded()
    {
        var configuration = CheckerConfiguration.Create().WithRandomGeneratorSeed(17).WithMaxUnfairSchedulingSteps(5);
        var strategy = new MonitorGuidedFeedbackStrategy(configuration, CreateMonitorAst());
        var trace = Run(strategy, observeLeader: true);
        strategy.ObserveRunningResults(new TimelineObserver(), trace);

        Assert.That(strategy.TotalSavedInputs(), Is.Zero);
        Assert.That(strategy.Statistics.TotalExecutions, Is.EqualTo(1));
        Assert.That(strategy.Statistics.ExecutionsWithViolationProgress, Is.EqualTo(1));
        Assert.That(strategy.Statistics.TotalAdmittedPrefixes, Is.Zero);
    }

    [Test]
    public void ViolatingChildImprovesBeyondParentEvenThoughItIsDiscarded()
    {
        var configuration = CheckerConfiguration.Create().WithRandomGeneratorSeed(17);
        var strategy = new MonitorGuidedFeedbackStrategy(configuration, CreateMonitorAst());
        var timeline = new TimelineObserver();
        strategy.ObserveRunningResults(timeline, Run(strategy, observeLeader: true));
        strategy.PrepareForNextIteration();

        var child = Run(strategy, observeLeader: true);
        Observe(strategy, new RunTaskChoice(0), child.Count, 8);
        strategy.ObserveRunningResults(timeline, child);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Statistics.TotalChildPrefixes, Is.EqualTo(1));
            Assert.That(strategy.Statistics.ChildPrefixesWithProgressImprovement, Is.EqualTo(1));
            Assert.That(strategy.Statistics.ChildPrefixProgressImprovementRate, Is.EqualTo(1));
            Assert.That(strategy.Statistics.MinChildPrefixProgressImprovement, Is.EqualTo(1));
            Assert.That(strategy.Statistics.MaxChildPrefixProgressImprovement, Is.EqualTo(1));
            Assert.That(strategy.Statistics.MeanChildPrefixProgressImprovement, Is.EqualTo(1));
            Assert.That(strategy.Statistics.MedianChildPrefixProgressImprovement, Is.EqualTo(1));
            Assert.That(strategy.TotalSavedInputs(), Is.EqualTo(1));
            Assert.That(strategy.Statistics.TotalAdmittedPrefixes, Is.EqualTo(1));
            Assert.That(strategy.Statistics.PrefixesAdmittedForDiversityAndProgress, Is.EqualTo(1));
            Assert.That(strategy.Statistics.DiversityAndProgressAdmissionRate, Is.EqualTo(1));
        });
    }

    [Test]
    public void AdmissionsUseNoveltyAndGlobalBestRatherThanJustNonzeroProgress()
    {
        var configuration = CheckerConfiguration.Create().WithRandomGeneratorSeed(17);
        var strategy = new MonitorGuidedFeedbackStrategy(configuration, CreateMonitorAst());
        var timeline = new TimelineObserver();
        strategy.ObserveRunningResults(timeline, Run(strategy, observeLeader: false));
        strategy.PrepareForNextIteration();
        strategy.ObserveRunningResults(timeline, Run(strategy, observeLeader: true));
        strategy.PrepareForNextIteration();
        strategy.ObserveRunningResults(timeline, Run(strategy, observeLeader: true));
        Assert.That(strategy.Statistics.TotalAdmittedPrefixes, Is.EqualTo(2));

        var novelTimeline = new TimelineObserver();
        var machine = new PChecker.Runtime.StateMachines.StateMachineId(typeof(MonitorGuidedReplayTests),
            "test", null, null, useNameForHashing: true);
        novelTimeline.OnDequeueEvent(machine, "state", PChecker.Runtime.Events.DefaultEvent.Instance);
        novelTimeline.OnDequeueEvent(machine, "state", PChecker.Runtime.Events.DefaultEvent.Instance);
        strategy.PrepareForNextIteration();
        strategy.ObserveRunningResults(novelTimeline, Run(strategy, observeLeader: true));

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Statistics.TotalAdmittedPrefixes, Is.EqualTo(strategy.TotalSavedInputs()));
            Assert.That(strategy.Statistics.TotalAdmittedPrefixes, Is.EqualTo(3));
            Assert.That(strategy.Statistics.PrefixesAdmittedForDiversityOnly, Is.EqualTo(2));
            Assert.That(strategy.Statistics.PrefixesAdmittedForProgressOnly, Is.EqualTo(1));
            Assert.That(strategy.Statistics.PrefixesAdmittedForDiversityAndProgress, Is.Zero);
            Assert.That(strategy.Statistics.DiversityOnlyAdmissionRate, Is.EqualTo(2.0 / 3));
            Assert.That(strategy.Statistics.ProgressOnlyAdmissionRate, Is.EqualTo(1.0 / 3));
            Assert.That(strategy.Statistics.TotalChildPrefixes, Is.EqualTo(3));
            Assert.That(strategy.Statistics.ChildPrefixesWithProgressImprovement, Is.EqualTo(3));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GuidanceInvalidationCountsOncePerExecutionAndResetsForNextRun(bool makeProgress)
    {
        var configuration = CheckerConfiguration.Create().WithRandomGeneratorSeed(17);
        var strategy = new MonitorGuidedFeedbackStrategy(configuration, CreateMonitorAst());
        var timeline = new TimelineObserver();
        var trace = Run(strategy, observeLeader: makeProgress);
        var choice = new RunTaskChoice(0);
        for (int i = 0; i < 3; i++)
        {
            strategy.NotifyEffects(choice,
                [new MonitorObservationEffect(null, new eOtherLeader(7), false, "OneLeaderPerElection",
                    i == 0 ? trace.Count - 1 : trace.Count)], true);
        }
        Observe(strategy, choice, trace.Count, 8);
        strategy.ObserveRunningResults(timeline, trace);

        Assert.Multiple(() =>
        {
            Assert.That(strategy.Statistics.TotalExecutions, Is.EqualTo(1));
            Assert.That(strategy.Statistics.ExecutionsInvalidatingGuidance, Is.EqualTo(1));
            Assert.That(strategy.Statistics.MinGuidanceInvalidationStep, Is.EqualTo(trace.Count - 1));
            Assert.That(strategy.Statistics.MaxGuidanceInvalidationStep, Is.EqualTo(trace.Count - 1));
            Assert.That(strategy.Statistics.MeanGuidanceInvalidationStep, Is.EqualTo(trace.Count - 1));
            Assert.That(strategy.Statistics.MedianGuidanceInvalidationStep, Is.EqualTo(trace.Count - 1));
            Assert.That(strategy.Statistics.ExecutionsWithViolationProgress, Is.EqualTo(makeProgress ? 1 : 0));
            Assert.That(strategy.Statistics.RootPrefixes, Is.EqualTo(1));
        });

        strategy.PrepareForNextIteration();
        strategy.ObserveRunningResults(timeline, Run(strategy, observeLeader: makeProgress));
        Assert.That(strategy.Statistics.TotalExecutions, Is.EqualTo(2));
        Assert.That(strategy.Statistics.ExecutionsInvalidatingGuidance, Is.EqualTo(1));
        Assert.That(strategy.Statistics.MeanGuidanceInvalidationStep, Is.EqualTo(trace.Count - 1));
        Assert.That(strategy.Statistics.MedianGuidanceInvalidationStep, Is.EqualTo(trace.Count - 1));
        Assert.That(strategy.Statistics.RootPrefixes, Is.EqualTo(1));
    }

    [Test]
    public void FinalCheckerReportIncludesFeedbackStatisticsOnSeparateLines()
    {
        var configuration = CheckerConfiguration.Create().WithRandomGeneratorSeed(17);
        configuration.SchedulingStrategy = StrategyType.MonitorGuidedFeedback;
        configuration.MonitorASTs = new[] { CreateMonitorAst() };
        var engine = PChecker.SystematicTesting.TestingEngine.Create(configuration, (Action)FeedbackReportTest);
        var strategy = (MonitorGuidedFeedbackStrategy)engine.Strategy;
        strategy.ObserveRunningResults(new TimelineObserver(), Run(strategy, observeLeader: true));
        strategy.PrepareForNextIteration();

        var report = engine.GetReport();
        Assert.That(report, Does.Contain(Environment.NewLine + "..... Total saved: 1"));
        Assert.That(report, Does.Contain(Environment.NewLine + "..... Executions with non-zero violation progress: 1/1"));
        Assert.That(report, Does.Contain(Environment.NewLine + "..... Prefixes selected for mutation: 1"));
        Assert.That(report, Does.Contain(Environment.NewLine + "..... Selected prefix length: min=5, max=5"));
        Assert.That(report, Does.Contain(Environment.NewLine + "..... Solver cache: hits=0, misses=1"));
        Assert.That(report, Does.Contain(Environment.NewLine + "..... Child prefixes improving on their parent: 0/0"));
        Assert.That(report, Does.Contain(Environment.NewLine + "..... Child prefix progress improvement (violation-distance reduction): min=0"));
        Assert.That(report, Does.Contain(Environment.NewLine + "..... Admitted prefixes: 1; diversity only=0"));
        Assert.That(report, Does.Contain(Environment.NewLine + "..... Executions invalidating guidance: 0"));
        Assert.That(report, Does.Contain(Environment.NewLine + "..... Root prefixes: 1"));
    }

    [PChecker.SystematicTesting.Test(new[] { "OneLeaderPerElection" })]
    private static void FeedbackReportTest() { }

    private static ScheduleTrace Run(MonitorGuidedFeedbackStrategy strategy, bool observeLeader)
    {
        var trace = new ScheduleTrace();
        SchedulingChoice[] choices = [new RunTaskChoice(0), new RunTaskChoice(1), new RunTaskChoice(2)];
        strategy.GetNextSchedulingChoice(choices[0], choices, out var choice);
        trace.AddSchedulingChoice(choice.OperationId, choice.ChoiceId);
        strategy.GetNextBooleanChoice(choice, 2, out var randomBoolean);
        trace.AddNondeterministicBooleanChoice(randomBoolean);
        strategy.GetNextIntegerChoice(choice, int.MaxValue, out var randomInteger);
        trace.AddNondeterministicIntegerChoice(randomInteger);

        strategy.GetNextIntegerChoice(choice, int.MaxValue, out randomInteger);
        trace.AddNondeterministicIntegerChoice(randomInteger);
        strategy.GetNextSchedulingChoice(choice, choices, out choice);
        trace.AddSchedulingChoice(choice.OperationId, choice.ChoiceId);
        if (observeLeader)
        {
            Observe(strategy, choice, trace.Count, 7);
        }

        strategy.GetNextIntegerChoice(choice, int.MaxValue, out randomInteger);
        trace.AddNondeterministicIntegerChoice(randomInteger);
        return trace;
    }

    private static void Observe(MonitorGuidedFeedbackStrategy strategy, SchedulingChoice choice, int position, int leader) =>
        strategy.NotifyEffects(choice,
            [new MonitorObservationEffect(null, new eLeaderChosen(leader), false, "OneLeaderPerElection", position)], true);

    private static (ScheduleStepType, ulong, int, bool?, int?) Choice(ScheduleStep step) =>
        (step.Type, step.ScheduledOperationId, step.ChoiceId, step.BooleanChoice, step.IntegerChoice);

    private static Machine CreateMonitorAst()
    {
        var ast = new Machine("OneLeaderPerElection", new PParser.SpecMachineDeclContext(null, 0));
        var leaderChosen = new Event(nameof(eLeaderChosen), new PParser.EventDeclContext(null, 0))
        {
            PayloadType = new NamedTupleType([new NamedTupleEntry { Name = "leader", Type = PrimitiveType.Int }])
        };
        var observes = new NamedEventSet(string.Empty, null);
        observes.AddEvent(leaderChosen);
        ast.Observes = observes;
        ast.AddField(new Variable("chosenLeader") { Type = PrimitiveType.Int });

        var start = new State(new PParser.StateDeclContext(null, 0), "NoLeaderChosen") { IsStart = true };
        var chosen = new State(new PParser.StateDeclContext(null, 0), "LeaderChosen");
        start[leaderChosen] = new EventDoAction(null, leaderChosen, null);
        chosen[leaderChosen] = new EventDoAction(null, leaderChosen, null);
        ast.AddState(start);
        ast.AddState(chosen);
        ast.StartState = start;
        return ast;
    }

    private sealed class eLeaderChosen(int leader) : RuntimeEvent(new PNamedTuple(["leader"], new PInt(leader)));

    private sealed class eOtherLeader(int leader) : RuntimeEvent(new PNamedTuple(["leader"], new PInt(leader)));
}
