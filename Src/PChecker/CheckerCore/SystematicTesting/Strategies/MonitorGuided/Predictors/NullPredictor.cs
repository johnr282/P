using PChecker.Runtime.StateMachines;
using PChecker.SystematicTesting.Operations;
using ASTEvent = Plang.Compiler.TypeChecker.AST.Declarations.Event;
using RuntimeEvent = PChecker.Runtime.Events.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.Predictors
{
    /// <summary>
    /// Returns no predictions and ignores given observations.
    /// </summary>
    internal class NullPredictor : IPredictor
    {
        public IReadOnlyDictionary<BehaviorStore.BehaviorNode, List<RuntimeEvent>>
            MonitorObservedBehaviors => new Dictionary<BehaviorStore.BehaviorNode, List<RuntimeEvent>>();

        public void AttachStatistics(MonitorGuidedStatistics statistics) { }

        public void SetMonitorObservedEvents(IReadOnlyList<ASTEvent> observedEvents) { }

        /// <inheritdoc/>
        public bool PredictEffects(
            MachineCreationPath machinePath,
            IReadOnlyList<SchedulingChoice> currentTrace,
            SchedulingChoice candidateChoice,
            out IReadOnlyList<ExecutionEffect> predictedEffects)
        {
            predictedEffects = null;
            return false;
        }

        /// <inheritdoc/>
        public void AddObservation(
            MachineCreationPath machinePath,
            IReadOnlyList<SchedulingChoice> currentTrace,
            SchedulingChoice executedChoice,
            IReadOnlyList<ExecutionEffect> producedEffects,
            bool completeObservation)
        {
            // Intentionally empty.
        }
    }
}
