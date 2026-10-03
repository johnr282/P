using PChecker.Runtime.Events;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Solver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    internal class MonitorGuidance
    {
        public SymExpr ViolationCondition { get; set; }

        public IReadOnlyList<SymEvent> ViolatingExecution { get; }

        internal MonitorGuidance(
            IReadOnlyList<SymEvent> violatingExecution, 
            SymExpr violationCondition)
        {
            ViolatingExecution = violatingExecution;
            ViolationCondition = violationCondition;
        }
    }
}
