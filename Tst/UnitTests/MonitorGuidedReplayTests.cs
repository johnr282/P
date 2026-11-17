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
        var configuration = CheckerConfiguration.Create().WithRandomGeneratorSeed(seed);
        var strategy = new MonitorGuidedFeedbackStrategy(configuration, CreateMonitorAst());
        var timeline = new TimelineObserver();
        var original = Run(strategy, secondObservation: false);
        strategy.ObserveRunningResults(timeline, original);

        strategy.PrepareForNextIteration();
        var child = Run(strategy, secondObservation: true);
        Assert.That(child.Take(3).Select(Choice), Is.EqualTo(original.Take(3).Select(Choice)));
        var seeds = new Random(seed);
        var schedulerSeed = seeds.Next();
        var inputSeed = seeds.Next();
        Assert.That(child[3].IntegerChoice, Is.EqualTo(new Random(inputSeed).Next()));
        Assert.That(child[4].ScheduledOperationId, Is.EqualTo((ulong)(new Random(schedulerSeed).Next() % 3)));
        strategy.ObserveRunningResults(timeline, child);
        Assert.That(strategy.TotalSavedInputs(), Is.EqualTo(2));

        // Exhaust the initial parent's batch so the child becomes the next parent.
        for (int i = 1; i < 50; i++)
        {
            strategy.PrepareForNextIteration();
            strategy.ObserveRunningResults(timeline, Run(strategy, secondObservation: false));
            seeds.Next();
            seeds.Next();
        }

        strategy.PrepareForNextIteration();
        var grandchild = Run(strategy, secondObservation: true);
        Assert.That(grandchild.Take(5).Select(Choice), Is.EqualTo(child.Take(5).Select(Choice)));
        seeds.Next();
        Assert.That(grandchild[5].IntegerChoice, Is.EqualTo(new Random(seeds.Next()).Next()));
        Assert.DoesNotThrow(() => strategy.ObserveRunningResults(timeline, grandchild));
    }

    private static ScheduleTrace Run(MonitorGuidedFeedbackStrategy strategy, bool secondObservation)
    {
        var trace = new ScheduleTrace();
        SchedulingChoice[] choices = [new RunTaskChoice(0), new RunTaskChoice(1), new RunTaskChoice(2)];
        strategy.GetNextSchedulingChoice(choices[0], choices, out var choice);
        trace.AddSchedulingChoice(choice.OperationId, choice.ChoiceId);
        strategy.GetNextBooleanChoice(choice, 2, out var randomBoolean);
        trace.AddNondeterministicBooleanChoice(randomBoolean);
        strategy.GetNextIntegerChoice(choice, int.MaxValue, out var randomInteger);
        trace.AddNondeterministicIntegerChoice(randomInteger);
        Observe(strategy, choice, trace.Count, 7);

        strategy.GetNextIntegerChoice(choice, int.MaxValue, out randomInteger);
        trace.AddNondeterministicIntegerChoice(randomInteger);
        strategy.GetNextSchedulingChoice(choice, choices, out choice);
        trace.AddSchedulingChoice(choice.OperationId, choice.ChoiceId);
        if (secondObservation)
        {
            Observe(strategy, choice, trace.Count, 8);
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
