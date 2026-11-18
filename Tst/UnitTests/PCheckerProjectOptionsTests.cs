using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using NUnit.Framework;
using PChecker.Configuration;
using Plang.Options;
using Debug = PChecker.IO.Debugging.Debug;

namespace UnitTests;

[TestFixture]
public class PCheckerProjectOptionsTests
{
    [TestCase("--pproj", null, MonitorGuidedFeedbackBoundaryMode.RandomExecution)]
    [TestCase("-pp", "best-prefix", MonitorGuidedFeedbackBoundaryMode.BestPrefix)]
    [TestCase("--pproj", "random-best-prefix", MonitorGuidedFeedbackBoundaryMode.RandomBestPrefix)]
    [TestCase("--pproj", "random-execution", MonitorGuidedFeedbackBoundaryMode.RandomExecution)]
    public void ExplicitProjectSupportsMonitorGuidedFeedbackAndDebugFlags(
        string projectOption, string boundaryOption, MonitorGuidedFeedbackBoundaryMode expectedBoundary)
    {
        string directory = Path.Combine(Path.GetTempPath(), "p-checker-project-" + Guid.NewGuid().ToString("N"));
        bool previousDebug = Debug.IsEnabled;
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "Main.p");
            File.WriteAllText(source, "machine Main { start state Init { } }");
            string project = Path.Combine(directory, "Selected.pproj");
            string generated = Path.Combine(directory, "generated");
            File.WriteAllText(project, new XElement("Project",
                new XElement("ProjectName", "Selected"),
                new XElement("InputFiles", new XElement("PFile", source)),
                new XElement("OutputDir", generated)).ToString());
            File.WriteAllText(Path.Combine(directory, "Other.pproj"),
                "<Project><ProjectName>Other</ProjectName><OutputDir>./other-output/</OutputDir></Project>");
            string compiled = Path.Combine(generated, "PChecker", "net8.0");
            Directory.CreateDirectory(compiled);
            string assembly = Path.Combine(compiled, "Selected.dll");
            File.WriteAllBytes(assembly, Array.Empty<byte>());

            var arguments = new List<string>
            {
                projectOption, project, "--testcase", "tcOneLeaderElection1",
                "--sch-monitorguidedfeedback", "--inbox-type", "eventqueue",
                "--schedules", "1000", "--debug", "--verbose",
                "--outdir", Path.Combine(directory, "logs")
            };
            if (boundaryOption != null)
            {
                arguments.Add("--sch-monitorguidedfeedback-boundary");
                arguments.Add(boundaryOption);
            }

            var configuration = new PCheckerOptions().Parse(arguments.ToArray());

            Assert.Multiple(() =>
            {
                Assert.That(configuration.AssemblyToBeAnalyzed, Is.EqualTo(assembly));
                Assert.That(Path.TrimEndingDirectorySeparator(configuration.PCompiledPath), Is.EqualTo(generated));
                Assert.That(configuration.CompilerConfig.ProjectName, Is.EqualTo("Selected"));
                Assert.That(configuration.CompilerConfig.InputPFiles, Does.Contain(source));
                Assert.That(configuration.TestCaseName, Is.EqualTo("tcOneLeaderElection1"));
                Assert.That(configuration.SchedulingStrategy, Is.EqualTo(StrategyType.MonitorGuidedFeedback));
                Assert.That(configuration.MonitorGuidedFeedbackBoundary, Is.EqualTo(expectedBoundary));
                Assert.That(configuration.InboxType, Is.EqualTo(InboxType.EventQueue));
                Assert.That(configuration.TestingIterations, Is.EqualTo(1000));
                Assert.That(configuration.EnableDebugging, Is.True);
                Assert.That(configuration.IsVerbose, Is.True);
                Assert.That(Debug.IsEnabled, Is.True);
            });
        }
        finally
        {
            Debug.IsEnabled = previousDebug;
            Directory.Delete(directory, recursive: true);
        }
    }
}
