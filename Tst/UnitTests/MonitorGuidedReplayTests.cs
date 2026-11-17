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
    }

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
}
