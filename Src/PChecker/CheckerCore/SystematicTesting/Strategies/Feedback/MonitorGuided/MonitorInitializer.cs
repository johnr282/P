using PChecker.Configuration;
using PChecker.IO.Debugging;
using Plang.Compiler;
using Plang.Compiler.TypeChecker.AST.Declarations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.Feedback.MonitorGuided
{
    internal static class MonitorInitializer
    {
        internal static Machine GetMonitorForFeedback(CheckerConfiguration configuration, TestMethodInfo test)
        {
            var names = test.MonitorNames;
            if (names == null)
            {
                throw new ArgumentException($"Test '{test.Name}' has no generated P monitor metadata. " +
                    "Recompile the P project with this version of the compiler.");
            }

            if (names.Count != 1)
            {
                throw new ArgumentException($"Monitor-guided feedback requires exactly one monitor. " +
                    $"Test '{test.Name}' configures {names.Count}.");
            }

            var matches = configuration.MonitorASTs?
                .Where(monitor => string.Equals(monitor.Name, names[0], StringComparison.Ordinal)).ToList();
            if (matches == null || matches.Count != 1)
            {
                throw new ArgumentException($"Expected one P declaration for monitor '{names[0]}' of test " +
                    $"'{test.Name}'. Check that the .pproj matches the compiled test assembly.");
            }

            return matches[0];
        }

        /// <summary>
        /// Performs parsing and type checking of the P program to extract the 
        /// monitor ASTs.
        /// </summary>
        internal static void CompileMonitors(CheckerConfiguration configuration)
        {
            if (!Compiler.ParseAndTypeCheck(configuration.CompilerConfig, out var scope))
            {
                Error.CheckerReportAndExit(
                    "Parsing and type checking during monitor analysis for " +
                    "monitor-guided strategy failed.");
                return;
            }

            configuration.MonitorASTs = scope.Machines.Where(m => m.IsSpec).ToList();
        }

        /// <summary>
        /// Returns the monitor AST from the given configuration with the given name.
        /// </summary>
        internal static Machine GetCorrespondingMonitorAST(
            CheckerConfiguration configuration,
            string monitorASTName)
        {
            var matchingASTs = configuration.MonitorASTs
                .Where(m => m.Name.Equals(monitorASTName)).ToList();

            if (matchingASTs.Count == 0)
            {
                Error.ReportAndExit(
                    $"No monitor AST found for monitor '{monitorASTName}'.");
            }
            else if (matchingASTs.Count > 1)
            {
                Error.ReportAndExit(
                    $"Multiple monitor ASTs found for monitor '{monitorASTName}'.");
            }

            return matchingASTs[0];
        }
    }
}
