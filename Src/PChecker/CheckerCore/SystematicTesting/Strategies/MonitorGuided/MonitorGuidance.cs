using PChecker.Runtime.Events;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    internal class MonitorGuidance
    {
        private readonly List<SymEvent> _violatingExecution;

        private readonly SymExpr _violationCondition;

        internal MonitorGuidance(List<SymEvent> violatingExecution, SymExpr violationCondition)
        {
            _violatingExecution = violatingExecution;
            _violationCondition = violationCondition;
        }

        /// <summary>
        /// Returns whether e is a goal event at the given index.
        /// </summary>
        internal bool IsGoalEvent(Event e, int goalIndex)
        {
            throw new NotImplementedException();
        }
    }
}
