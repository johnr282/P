using PChecker.Runtime.StateMachines;
using PChecker.SystematicTesting.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided.Predictors
{
    /// <summary>
    /// Returns a prediction only when an exact match is found with a previously
    /// seen observation. 
    /// </summary>
    internal class ExactPredictor : IPredictor
    {
        private readonly Dictionary<MachineCreationPath, BehaviorStore>
            _behaviorStores = new();

        /// <inheritdoc/>
        public bool PredictEffects(
            MachineCreationPath machinePath,
            IReadOnlyList<SchedulingChoice> currentTrace,
            SchedulingChoice candidateChoice,
            out IReadOnlyList<ExecutionEffect> predictedEffects)
        {
            if (!_behaviorStores.TryGetValue(machinePath, out var behaviorStore))
            {
                predictedEffects = null;
                return false;
            }
                
            // Ignore whether the behavior is complete or not; an incomplete 
            // prediction is better than no prediction
            return behaviorStore.GetBehavior(
                currentTrace,
                candidateChoice,
                out predictedEffects,
                out bool _);
        }

        /// <inheritdoc/>
        public void AddObservation(
            MachineCreationPath machinePath,
            IReadOnlyList<SchedulingChoice> currentTrace,
            SchedulingChoice executedChoice,
            IReadOnlyList<ExecutionEffect> producedEffects,
            bool completeObservation)
        {
            if (!_behaviorStores.TryGetValue(machinePath, out var behaviorStore))
            {
                behaviorStore = new BehaviorStore();
                _behaviorStores[machinePath] = behaviorStore;
            }

            behaviorStore.AddBehavior(
                currentTrace,
                executedChoice,
                producedEffects,
                completeObservation);
        }
    }
}
