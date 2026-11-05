using PChecker.Runtime.StateMachines;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Strategies.MonitorGuided;
using Plang.Compiler.TypeChecker.AST.Declarations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ASTEvent = Plang.Compiler.TypeChecker.AST.Declarations.Event;
using RuntimeEvent = PChecker.Runtime.Events.Event;

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
        private readonly HashSet<BehaviorStore.BehaviorNode>
            _monitorObservedBehaviors = new();

        private MonitorGuidedStatistics _statistics;
        private IReadOnlyList<ASTEvent> _observedEventTypes;

        internal ExactPredictor(MonitorGuidedStatistics statistics = null)
            => _statistics = statistics;
        public void AttachStatistics(MonitorGuidedStatistics statistics)
            => _statistics = statistics;

        public void SetMonitorObservedEvents(IReadOnlyList<ASTEvent> observedEvents)
            => _observedEventTypes = observedEvents;

        public IReadOnlySet<BehaviorStore.BehaviorNode>
            MonitorObservedBehaviors => _monitorObservedBehaviors;

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
                behaviorStore = new BehaviorStore(machinePath);
                _behaviorStores[machinePath] = behaviorStore;
            }

            bool behaviorAccepted = behaviorStore.AddBehavior(
                currentTrace,
                executedChoice,
                producedEffects,
                completeObservation,
                out var behaviorNode,
                out bool behaviorChanged,
                out bool matchedExistingBehavior);

            if (!behaviorAccepted)
            {
                _statistics?.RecordInconsistentBehavior();
                return;
            }

            if (matchedExistingBehavior)
            {
                _statistics?.RecordConsistentBehavior();
            }

            if (!behaviorChanged) return;

            var observedEvents = GetMonitorObservedEvents(behaviorNode);
            bool wasMonitorObserved = behaviorNode.MonitorObservedEvents.Count > 0;
            behaviorNode.MonitorObservedEvents = observedEvents;
            if (!wasMonitorObserved && observedEvents.Count > 0)
                AddMonitorObservedBehavior(behaviorNode);
        }

        private void AddMonitorObservedBehavior(
            BehaviorStore.BehaviorNode behaviorNode)
        {
            _monitorObservedBehaviors.Add(behaviorNode);

            var nearestObservedAncestor =
                behaviorNode.LatestMonitorObservedAncestor;
            if (nearestObservedAncestor != null)
            {
                _monitorObservedBehaviors.Remove(nearestObservedAncestor);
            }
        }

        private List<RuntimeEvent> GetMonitorObservedEvents(
            BehaviorStore.BehaviorNode behaviorNode)
        {
            List<RuntimeEvent> observedEvents = new();
            foreach (var effect in behaviorNode.Effects)
            {
                if (effect is not MonitorObservationEffect observation)
                    continue;

                if (_observedEventTypes != null && _observedEventTypes.Any(ev =>
                    MonitorGuidedStrategy.EventTypeMatches(ev, observation.ObservedEvent)))
                {
                    observedEvents.Add(observation.ObservedEvent);
                }
            }

            return observedEvents;
        }
    }
}
