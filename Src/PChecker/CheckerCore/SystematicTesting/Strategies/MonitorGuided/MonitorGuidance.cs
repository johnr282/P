using PChecker.Runtime.Events;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Solver;
using Plang.Compiler.TypeChecker.AST.States;
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

        /// <summary>
        /// At s = ViolatingExecution[i], the monitor was in state s.state and 
        /// received event s.e.
        /// </summary>
        private IReadOnlyList<(SymEvent e, State state)> _violatingExecution;

        public MonitorGuidance(
            IReadOnlyList<(SymEvent e, State state)> violatingExecution, 
            SymExpr violationCondition)
        {
            _violatingExecution = violatingExecution;
            ViolationCondition = violationCondition;
        }

        public SymEvent GetNextViolatingEvent(int guidanceIndex)
        {
            return _violatingExecution[guidanceIndex].e;
        }

        public State GetCurrentMonitorState(int guidanceIndex)
        {
            return _violatingExecution[guidanceIndex].state;
        }

        public bool FinalGuidanceIndex(int guidanceIndex)
        {
            return guidanceIndex == _violatingExecution.Count - 1;
        }
    }
}
