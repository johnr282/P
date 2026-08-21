using PChecker.Configuration;
using Plang.Compiler;
using PChecker.Runtime.Exceptions;
using PChecker.IO.Debugging;
using Plang.Compiler.TypeChecker.AST.Declarations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    static internal class MonitorAnalysis
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
    }
}
