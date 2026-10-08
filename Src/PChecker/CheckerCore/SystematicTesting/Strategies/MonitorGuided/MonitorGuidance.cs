using PChecker.Runtime.Events;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Solver;
using Plang.Compiler.TypeChecker.AST.States;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    internal class MonitorGuidance
    {
        public SymExpr ViolationCondition { get; set; }

        public int GuidanceIndex { get; set; } = 0;

        public bool Valid { get; set; } = true;

        /// <summary>
        /// At s = ViolatingExecution[i], the monitor was in state s.state and 
        /// received event s.e.
        /// </summary>
        private readonly IReadOnlyList<(SymEvent e, State state)> _violatingExecution;

        public MonitorGuidance(
            IReadOnlyList<(SymEvent e, State state)> violatingExecution, 
            SymExpr violationCondition)
        {
            _violatingExecution = violatingExecution;
            ViolationCondition = violationCondition;
        }

        public SymEvent GetNextGoalEvent() =>
            _violatingExecution[GuidanceIndex].e;

        public State GetCurrentMonitorState() => 
            _violatingExecution[GuidanceIndex].state;

        public bool ViolationReached => GuidanceIndex == _violatingExecution.Count;

        public MonitorGuidance Clone()
        {
            var violatingExecution = _violatingExecution
                .Select(step => (
                    new SymEvent(step.e.Event, SymExprFactory.CloneSymExpr(step.e.Payload)),
                    step.state))
                .ToImmutableArray();

            return new MonitorGuidance(
                violatingExecution,
                SymExprFactory.CloneSymExpr(ViolationCondition))
            {
                GuidanceIndex = this.GuidanceIndex
            };
        }
    }
}
