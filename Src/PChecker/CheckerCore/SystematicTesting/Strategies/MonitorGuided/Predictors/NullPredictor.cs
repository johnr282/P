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
    /// Returns no predictions and ignores given observations.
    /// </summary>
    internal class NullPredictor : IPredictor
    {
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
