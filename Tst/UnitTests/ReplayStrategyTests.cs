using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using NUnit.Framework;
using PChecker.Runtime;
using PChecker.Runtime.Events;
using PChecker.Runtime.Logging;
using PChecker.Runtime.Specifications;
using PChecker.Runtime.StateMachines;
using PChecker.SystematicTesting;
using ControlledRuntime = PChecker.SystematicTesting.ControlledRuntime;
using NUnitTest = NUnit.Framework.TestAttribute;
using PMonitor = PChecker.Runtime.Specifications.Monitor;
using PTest = PChecker.SystematicTesting.TestAttribute;

namespace UnitTests;

[TestFixture]
[NonParallelizable]
public class ReplayStrategyTests
{
    [NUnitTest]
    public void ReplayUsesScheduleFileToReproduceViolationAndRuntimeTrace()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReplayStrategy_" + Guid.NewGuid().ToString("N"));
        var randomOutput = Path.Combine(root, "random");
        var replayOutput = Path.Combine(root, "replay");
        Directory.CreateDirectory(randomOutput);
        Directory.CreateDirectory(replayOutput);

        try
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var testCaseName = nameof(ReplayStrategyRegressionCase);
            var checkerPath = Environment.GetEnvironmentVariable("PCHECKER_CLI_PATH") ??
                              Path.Combine(Path.GetDirectoryName(typeof(PChecker.Checker).Assembly.Location)!, "p.dll");
            var randomResult = RunChecker(checkerPath, assemblyPath, testCaseName, randomOutput,
                "--sch-random", "--seed", "13", "--schedules", "1");
            Assert.That(randomResult, Does.Contain("Found 1 bug."));

            var randomTracePath = FindOutput(randomOutput, ".trace.json");
            var schedulePath = FindOutput(randomOutput, ".schedule");
            var replayResult = RunChecker(checkerPath, assemblyPath, testCaseName, replayOutput,
                "--replay", schedulePath);
            Assert.That(replayResult, Does.Contain("Reproduced 1 bug."));

            var replayTracePath = FindOutput(replayOutput, ".trace.json");
            var randomTrace = JsonNode.Parse(File.ReadAllText(randomTracePath))!.AsArray();
            var replayTrace = JsonNode.Parse(File.ReadAllText(replayTracePath))!.AsArray();

            Assert.That(HasStrategyLabel(randomTrace, "random"), Is.True,
                $"Random trace should retain its strategy label. Entries: {string.Join(", ", randomTrace.Select(entry => entry?["type"]?.ToString()))}");
            Assert.That(HasStrategyLabel(replayTrace, "replay"), Is.True,
                $"Replay trace should retain its strategy label. Entries: {string.Join(", ", replayTrace.Select(entry => entry?["type"]?.ToString()))}");
            var normalizedRandomTrace = WithoutStrategyLabels(randomTrace);
            var normalizedReplayTrace = WithoutStrategyLabels(replayTrace);
            Assert.That(JsonNode.DeepEquals(normalizedRandomTrace, normalizedReplayTrace), Is.True,
                DescribeTraceDifference(normalizedRandomTrace, normalizedReplayTrace));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string RunChecker(string checkerPath, string assemblyPath, string testCaseName,
        string outputDirectory, params string[] strategyArguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(checkerPath);
        startInfo.ArgumentList.Add("check");
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("--testcase");
        startInfo.ArgumentList.Add(testCaseName);
        startInfo.ArgumentList.Add("--max-steps");
        startInfo.ArgumentList.Add("100");
        startInfo.ArgumentList.Add("--inbox-type");
        startInfo.ArgumentList.Add("eventset");
        startInfo.ArgumentList.Add("--outdir");
        startInfo.ArgumentList.Add(outputDirectory);
        foreach (var argument in strategyArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the P checker process.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
    }

    private static string FindOutput(string directory, string extension) =>
        Directory.GetFiles(directory, "*" + extension, SearchOption.AllDirectories).Single();

    private static bool HasStrategyLabel(JsonArray trace, string strategy) => trace.Any(entry =>
        entry?["type"]?.GetValue<string>() == "StrategyDescription" &&
        entry["details"]?["strategy"]?.GetValue<string>() == strategy);

    private static JsonArray WithoutStrategyLabels(JsonArray trace)
    {
        var normalized = new JsonArray();
        foreach (var entry in trace)
        {
            if (entry?["type"]?.GetValue<string>() != "StrategyDescription")
            {
                normalized.Add(entry?.DeepClone());
            }
        }

        return normalized;
    }

    private static string DescribeTraceDifference(JsonArray randomTrace, JsonArray replayTrace)
    {
        var index = 0;
        while (index < randomTrace.Count && index < replayTrace.Count &&
               JsonNode.DeepEquals(randomTrace[index], replayTrace[index]))
        {
            index++;
        }

        return $"Replay should reproduce the runtime trace, ignoring the strategy label. " +
               $"Random entries: {randomTrace.Count}; replay entries: {replayTrace.Count}; " +
               $"first differing entry {index}. Random: {randomTrace.ElementAtOrDefault(index)} " +
               $"Replay: {replayTrace.ElementAtOrDefault(index)}";
    }

    public sealed class ReplayStrategyRegressionCase
    {
        [PTest]
        public static void Execute(ControlledRuntime runtime)
        {
            runtime.RegisterLog(new PCheckerLogJsonFormatter());
            runtime.TryCreateMonitor(typeof(ReplayStrategyViolationMonitor));
            runtime.CreateStateMachine(typeof(ReplayStrategyViolationMachine), "ReplayStrategyViolationMachine");
        }
    }

    public sealed class ReplayViolationEvent : Event { }

    public sealed class ReplayStrategyViolationMonitor : PMonitor
    {
        [Start]
        [OnEventDoAction(typeof(ReplayViolationEvent), nameof(Fail))]
        private sealed class Initial : State { }

        private void Fail(Event e) => Assert(false, "Expected replay regression violation.");
    }

    public sealed class ReplayStrategyViolationMachine : StateMachine
    {
        [Start]
        [OnEntry(nameof(AnnounceViolation))]
        private sealed class Initial : State { }

        private void AnnounceViolation(Event e) => Monitor<ReplayStrategyViolationMonitor>(new ReplayViolationEvent());
    }
}
