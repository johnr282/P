using PChecker.Configuration;
using PChecker.IO.Debugging;
using Plang.Compiler;
using Plang.Compiler.TypeChecker.AST.Declarations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    internal static class MonitorInitializer
    {
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
