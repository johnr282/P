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
    /// Interface for a predictor used by the monitor-guided scheduler to predict
    /// possible effects of its choices. 
    /// </summary>
    internal interface IPredictor
    {
        /// <summary>
        /// Predicts the execution effects produced by executing candidateChoice
        /// on the state machine identified by machinePath with local trace 
        /// currentTrace.
        /// </summary>
        /// <param name="machinePath">
        /// Identifies machine whose effects to predict. 
        /// </param>
        /// <param name="currentTrace">Machine's current local trace.</param>
        /// <param name="candidateChoice">Choice to be executed on machine.</param>
        /// <param name="predictedEffects">
        /// The effects predicted to be produced by the machine in production order. 
        /// </param>
        /// <returns>
        /// True if a prediction was made, false otherwise. If false is returned, 
        /// predictedEffects will be null. 
        /// </returns>
        public bool PredictEffects(
            MachineCreationPath machinePath,
            IReadOnlyList<SchedulingChoice> currentTrace,
            SchedulingChoice candidateChoice,
            out IReadOnlyList<ExecutionEffect> predictedEffects);

        /// <summary>
        /// Notifies predictor of a runtime observation.
        /// </summary>
        /// <param name="machinePath">Identifies observed machine.</param>
        /// <param name="currentTrace">Machine's current local trace.</param>
        /// <param name="executedChoice">Choice executed on machine.</param>
        /// <param name="producedEffects">
        /// <param name="completeObservation">
        /// True if observation is complete, false otherwise.
        /// </param>
        /// Effects in production order produced by machine with local trace 
        /// currentTrace when executedChoice was executed.
        /// </param>
        public void AddObservation(
            MachineCreationPath machinePath,
            IReadOnlyList<SchedulingChoice> currentTrace,
            SchedulingChoice executedChoice,
            IReadOnlyList<ExecutionEffect> producedEffects,
            bool completeObservation);
    }
}
