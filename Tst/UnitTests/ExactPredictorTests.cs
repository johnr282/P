using System;
using System.Collections.Generic;
using NUnit.Framework;
using PChecker.Configuration;
using PChecker.Random;
using PChecker.Runtime.Events;
using PChecker.Runtime.StateMachines;
using PChecker.Runtime.Values;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Strategies.MonitorGuided.Predictors;
using PChecker.SystematicTesting.Strategies.Probabilistic;
using ControlledRuntime = PChecker.SystematicTesting.ControlledRuntime;

namespace UnitTests;

[TestFixture]
public class ExactPredictorTests
{
    private static ControlledRuntime NewRuntime()
    {
        var configuration = CheckerConfiguration.Create();
        return new ControlledRuntime(configuration, new RandomStrategy(10, new RandomValueGenerator(configuration)));
    }

    private static StateMachineId MachineId(ControlledRuntime runtime) =>
        new(typeof(TestMachine), "Machine", new MachineCreationPath(new uint[] { 0 }), runtime);

    [TestCase(0)] // Unknown machine
    [TestCase(1)] // Known machine, unseen candidate
    [TestCase(2)] // Known machine, unseen trace
    public void MissingPredictionsAlwaysReturnNull(int missKind)
    {
        using var runtime = NewRuntime();
        var id = MachineId(runtime);
        var initial = new InitializeChoice(id.Value, id, null);
        var resume = new ResumeInitializationChoice(id.Value, id, null);
        var predictor = new ExactPredictor();
        if (missKind != 0)
        {
            predictor.AddObservation(id.CreationPath, Array.Empty<SchedulingChoice>(), initial,
                Array.Empty<ExecutionEffect>(), true);
        }

        IReadOnlyList<SchedulingChoice> trace = missKind == 2
            ? new SchedulingChoice[] { resume } : Array.Empty<SchedulingChoice>();
        SchedulingChoice candidate = missKind == 1 ? resume : initial;
        Assert.That(predictor.PredictEffects(id.CreationPath, trace, candidate, out var effects), Is.False);
        Assert.That(effects, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RecordedEmptyEffectsAreAPrediction(bool complete)
    {
        using var runtime = NewRuntime();
        var id = MachineId(runtime);
        var choice = new InitializeChoice(id.Value, id, null);
        var predictor = new ExactPredictor();
        predictor.AddObservation(id.CreationPath, Array.Empty<SchedulingChoice>(), choice,
            Array.Empty<ExecutionEffect>(), complete);

        Assert.That(predictor.PredictEffects(id.CreationPath, Array.Empty<SchedulingChoice>(), choice,
            out var effects), Is.True);
        Assert.That(effects, Is.Not.Null.And.Empty);
    }

    [Test]
    public void PredictionAppendsCandidateToReadOnlyTraceWithoutMutatingIt()
    {
        using var firstRuntime = NewRuntime();
        using var secondRuntime = NewRuntime();
        var firstId = MachineId(firstRuntime);
        MachineId(secondRuntime); // Shift the runtime ID while keeping logical identity stable.
        var secondId = MachineId(secondRuntime);
        var initial = new InitializeChoice(firstId.Value, firstId, null);
        var resume = new ResumeInitializationChoice(firstId.Value, firstId, null);
        var predictor = new ExactPredictor();
        var expected = new ExecutionEffect[] { new MonitorObservationEffect(firstId, new Event(new PInt(7)), ignored: false) };
        predictor.AddObservation(firstId.CreationPath, Array.Empty<SchedulingChoice>(), initial,
            Array.Empty<ExecutionEffect>(), true);
        predictor.AddObservation(firstId.CreationPath, new[] { initial }, resume, expected, true);

        var currentInitial = new InitializeChoice(secondId.Value, secondId, null);
        var candidate = new ResumeInitializationChoice(secondId.Value, secondId, null);
        var trace = new List<SchedulingChoice> { currentInitial }.AsReadOnly();
        Assert.That(firstId.Value, Is.Not.EqualTo(secondId.Value));
        Assert.That(predictor.PredictEffects(secondId.CreationPath, trace, candidate, out var effects), Is.True);
        Assert.That(effects, Is.SameAs(expected));
        Assert.That(trace, Has.Count.EqualTo(1));
        Assert.That(trace[0], Is.SameAs(currentInitial));
        Assert.That(predictor.PredictEffects(secondId.CreationPath, Array.Empty<SchedulingChoice>(), candidate,
            out effects), Is.False, "The candidate must be looked up after the supplied history.");
        Assert.That(effects, Is.Null);
    }

    [Test]
    public void IncompletePredictionIsUpgradedWhenCompleteObservationArrives()
    {
        using var runtime = NewRuntime();
        var id = MachineId(runtime);
        var choice = new InitializeChoice(id.Value, id, null);
        var predictor = new ExactPredictor();
        var prefix = new ExecutionEffect[] { new MonitorObservationEffect(id, new Event(new PInt(1)), ignored: false) };
        var full = new ExecutionEffect[]
        {
            new MonitorObservationEffect(id, new Event(new PInt(1)), ignored: false),
            new MonitorObservationEffect(id, new Event(new PInt(2)), ignored: false)
        };
        predictor.AddObservation(id.CreationPath, Array.Empty<SchedulingChoice>(), choice, prefix, false);
        Assert.That(predictor.PredictEffects(id.CreationPath, Array.Empty<SchedulingChoice>(), choice,
            out var effects), Is.True);
        Assert.That(effects, Is.SameAs(prefix));

        predictor.AddObservation(id.CreationPath, Array.Empty<SchedulingChoice>(), choice, full, true);
        Assert.That(predictor.PredictEffects(id.CreationPath, Array.Empty<SchedulingChoice>(), choice,
            out effects), Is.True);
        Assert.That(effects, Is.SameAs(full));
    }

    private sealed class TestMachine : StateMachine { }
}
