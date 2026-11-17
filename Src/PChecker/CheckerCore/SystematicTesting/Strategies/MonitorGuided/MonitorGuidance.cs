using Microsoft.Z3;
using PChecker.Runtime.Events;
using PChecker.SystematicTesting.Strategies.Feedback.MonitorGuided;
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
        private SymExpr _violationCondition;
        private int _guidanceIndex = 0;

        /// <summary>
        /// At s = ViolatingExecution[i], the monitor was in state s.state and 
        /// received event s.e.
        /// </summary>
        private readonly IReadOnlyList<(SymEvent e, State state)> _violatingExecution;

        public bool Valid { get; private set; } = true;


        public MonitorGuidance(
            IReadOnlyList<(SymEvent e, State state)> violatingExecution, 
            SymExpr violationCondition)
        {
            _violatingExecution = violatingExecution;
            _violationCondition = violationCondition;
        }

        private SymEvent NextGoalEvent =>
            _violatingExecution[_guidanceIndex].e;

        public State CurrentMonitorState => 
            _violatingExecution[_guidanceIndex].state;

        public bool ViolationReached => _guidanceIndex == _violatingExecution.Count;

        public int DistanceToViolation => _violatingExecution.Count - _guidanceIndex;

        /// <summary>
        /// Updates guidance as if the monitor handled the given event.
        /// </summary>
        public void Update(Event e, ISolver solver)
        {
            if (ViolationReached || !Valid)
                return;

            SymEvent goalEvent = NextGoalEvent;

            var concretePayload = new ConcreteExpr(
                e.Payload?.Clone(),
                goalEvent.Payload.Type);

            var candidateCondition = SymExprFactory.And(
                _violationCondition,
                SymExprFactory.Equal(goalEvent.Payload, concretePayload));

            bool isGoalEvent = MonitorAnalyzer.EventTypeMatches(goalEvent.Event, e) &&
                solver.CheckSat(candidateCondition) == SatResult.Sat;

            if (isGoalEvent)
            {
                _guidanceIndex++;
                _violationCondition = candidateCondition;
                return;
            }

            Valid = false;
        }

        public MonitorGuidance Clone()
        {
            var violatingExecution = _violatingExecution
                .Select(step => (
                    new SymEvent(step.e.Event, SymExprFactory.CloneSymExpr(step.e.Payload)),
                    step.state))
                .ToImmutableArray();

            return new MonitorGuidance(
                violatingExecution,
                SymExprFactory.CloneSymExpr(_violationCondition))
            {
                _guidanceIndex = this._guidanceIndex
            };
        }
    }
}
