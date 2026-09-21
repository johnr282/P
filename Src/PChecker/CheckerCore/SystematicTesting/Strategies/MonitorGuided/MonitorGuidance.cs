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
        private readonly List<SymEvent> _violatingExecution;

        private readonly SymExpr _violationCondition;

        internal MonitorGuidance(List<SymEvent> violatingExecution, SymExpr violationCondition)
        {
            _violatingExecution = violatingExecution;
            _violationCondition = violationCondition;
        }

        /// <summary>
        /// Checks if e is a valid goal event according to the given goalIndex
        /// and currentCondition. 
        /// </summary>
        /// <param name="e"></param>
        /// <param name="goalIndex"></param>
        /// <param name="currentCondition"></param>
        /// <param name="solver">SMT solver used to check satisfiability.</param>
        /// <param name="nextCondition">
        /// If true is returned, updated condition with equality constraint 
        /// requiring event at goalIndex to be e. 
        /// If false is returned, equal to currentCondition.
        /// </param>
        /// <returns>
        /// True if e is confirmed to be a valid goal event, false if not (this 
        /// includes an Unknown result from the solver).
        /// </returns>
        internal bool CheckGoalEvent(
            Event e, 
            int goalIndex,
            SymExpr currentCondition,
            ISolver solver,
            out SymExpr nextCondition)
        {
            nextCondition = currentCondition;
            SymEvent goalEvent = _violatingExecution[goalIndex];

            if (!EventTypeMatches(goalEvent.Event, e))
                return false;

            var concretePayload = new ConcreteExpr(
                e.Payload?.Clone(),
                goalEvent.Payload.Type);

            var candidateCondition = SymExprFactory.And(
                currentCondition,
                SymExprFactory.Equal(goalEvent.Payload, concretePayload));

            if (solver.CheckSat(candidateCondition) == SatResult.Sat)
            {
                nextCondition = candidateCondition;
                return true;
            }

            return false;
        }

        private static bool EventTypeMatches(
            Plang.Compiler.TypeChecker.AST.Declarations.Event expected,
            PChecker.Runtime.Events.Event actual)
        {
            if (expected.IsHaltEvent)
                return actual is PHalt;

            if (expected.IsNullEvent)
                return actual is DefaultEvent;

            return string.Equals(
                expected.Name,
                actual.GetType().Name,
                StringComparison.Ordinal);
        }
    }
}
