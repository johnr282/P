using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Solver
{
    internal interface ISolver
    {
        /// <summary>
        /// Checks if the given formula is satisfiable.
        /// </summary>
        /// <returns>
        /// Sat if satisfiable, Unsat if unsatisfiable, Unknown if unknown.
        /// </returns>
        internal SatResult CheckSat(SymExpr formula);
    }

    internal enum SatResult
    {
        Sat, 
        Unsat, 
        Unknown
    }
}
