using LanguageExt;
using Microsoft.Z3;
using PChecker.IO.Debugging;
using PChecker.Random;
using PChecker.Runtime.Events;
using PChecker.Runtime.Specifications;
using PChecker.Runtime.StateMachines;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Strategies.MonitorGuided.Predictors;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Solver;
using Plang.Compiler.TypeChecker.AST.Declarations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using RuntimeEvent = PChecker.Runtime.Events.Event;
using ASTEvent = Plang.Compiler.TypeChecker.AST.Declarations.Event;
using ASTState = Plang.Compiler.TypeChecker.AST.States.State;
using Plang.Compiler.TypeChecker.AST.States;
using PChecker.Runtime.StateMachines.EventInboxes;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    internal class MonitorGuidedStrategy : ISchedulingStrategy
    {
        private readonly uint maxObservedEventsForMonitorExploration;
        private readonly uint maxRecursionDepthForProgressComputation;

        // For now, assume that there is only one monitor.
        private Monitor _monitor;
        private MonitorAnalyzer _monitorAnalyzer;

        private readonly Dictionary<MachineCreationPath, List<SchedulingChoice>> 
            _machineTraces = new();

        private readonly IPredictor _predictor;

        private readonly ISolver _solver;

        private MonitorGuidance _guidance;

        private int _guidanceIndex;

        private IRandomValueGenerator _random;

        public MonitorGuidedStrategy(IRandomValueGenerator random)
        {
            // JR TODO: Add these to CheckerConfiguration
            maxObservedEventsForMonitorExploration = 100;
            maxRecursionDepthForProgressComputation = 10;
            _predictor = new ExactPredictor();
            _solver = new Z3Solver();
            _random = random;
        }

        /// <summary>
        /// Registers the given monitor AST with its corresponding runtime 
        /// monitor instance and creates its analyzer.
        /// </summary>
        public void RegisterMonitor(Machine monitorAST, Monitor monitor)
        {
            if (_monitor != null)
            {
                Error.CheckerReportAndExit(
                    $"MonitorGuided strategy currently supports only one monitor. " +
                    $"Found multiple monitors: {_monitor.Name} and {monitorAST.Name}.");
                return;
            }

            if (monitor is not IMonitorFieldProvider fieldProvider)
            {
                Error.CheckerReportAndExit(
                    $"Monitor {monitorAST.Name} does not implement IMonitorFieldProvider " +
                    $"required by MonitorGuided strategy.");
                return;
            }

            _monitor = monitor;
            _monitorAnalyzer = new MonitorAnalyzer(
                monitorAST,
                fieldProvider,
                maxObservedEventsForMonitorExploration,
                MonitorAnalyzer.SearchStrategy.BFS);
        }

        /// <inheritdoc/>
        public virtual bool GetNextSchedulingChoice(
            SchedulingChoice lastChoice,
            IEnumerable<SchedulingChoice> choices,
            out SchedulingChoice next)
        {
            next = null;

            if (_guidance == null)
            {
                bool monitorViolationFound = _monitorAnalyzer.GetMonitorGuidance(
                    _monitor.CurrentStateName,
                    out _guidance);

                if (!monitorViolationFound)
                    return GetFallbackChoice(lastChoice, choices, out next);

                _guidanceIndex = 0;
            }

            int? maxProgressValue = null;
            List<SchedulingChoice> candidateChoices = new();
            foreach (var choice in choices)
            {
                int? progressValue = ComputeProgress(
                    choice,
                    choices,
                    _guidanceIndex,
                    _guidance.ViolationCondition,
                    _machineTraces,
                    recursionDepth: 1);

                if (progressValue == null)
                    continue;

                if (maxProgressValue == null || progressValue > maxProgressValue)
                {
                    maxProgressValue = progressValue;
                    candidateChoices.Clear();
                    candidateChoices.Add(choice);
                }
                else if (progressValue == maxProgressValue)
                {
                    candidateChoices.Add(choice);
                }
            }

            if (candidateChoices.Count == 0)
            {
                // Every choice invalidates the current guidance, so we need to
                // recompute it next time
                _guidance = null;
                return GetFallbackChoice(lastChoice, choices, out next);
            }

            // JR TODO: Could choose an unexplored choice if possible to 
            // gain as much behavior information as possible

            // JR TODO: Should the progress function return how long it takes 
            // each choice to make the returned progress? A choice immediately 
            // resulting in progress towards a violation may be better than a choice
            // leading to the same progress, but after many more additional choices.

            // Candidate choices all have equal non-null progress values, so they
            // are equally good choices; choose one at random
            next = candidateChoices[_random.Next(candidateChoices.Count)];
            return true;
        }

        private bool GetFallbackChoice(
            SchedulingChoice lastChoice,
            IEnumerable<SchedulingChoice> choices,
            out SchedulingChoice next)
        {
            // JR TODO
            throw new NotImplementedException();
        }


        private int? ComputeProgress(
            SchedulingChoice candidateChoice,
            IEnumerable<SchedulingChoice> choices,
            int guidanceIndex,
            SymExpr violationCondition,
            Dictionary<MachineCreationPath, List<SchedulingChoice>> machineTraces,
            int recursionDepth)
        {
            // JR TODO: Should RunTaskChoices get special handling? Computing
            // progress does not really make sense for them, so they would 
            // always have a progress value of 0. However, then they might
            // never be chosen, which could cause issues

            if (recursionDepth > maxRecursionDepthForProgressComputation ||
                candidateChoice is not StateMachineSchedulingChoice candidateMachineChoice)
                return 0;

            var candidatePath = candidateMachineChoice.GetCreationPath();
            var candidateTrace = machineTraces.TryGetValue(candidatePath, out var trace) 
                ? trace : new List<SchedulingChoice>();

            bool knownPrediction = _predictor.PredictEffects(
                candidatePath,
                candidateTrace,
                candidateMachineChoice,
                out var prediction);

            var newChoices = new List<SchedulingChoice>(choices);
            UpdateChoices(
                knownPrediction, 
                prediction, 
                candidateMachineChoice, 
                newChoices);

            // Update candidate machine's trace with the candidate choice for next
            // ComputeProgress call
            var newTraces = new Dictionary<MachineCreationPath,
                List<SchedulingChoice>>(machineTraces);
            var newTrace = new List<SchedulingChoice>(candidateTrace);
            newTrace.Add(candidateMachineChoice);
            newTraces[candidatePath] = newTrace;

            bool choiceResultsInGoalEvent = ChoiceResultsInGoalEvent(
                candidateMachineChoice,
                guidanceIndex,
                violationCondition,
                out var nextCondition);

            int? MaxFutureProgress(int nextGuidanceIndex) =>
                newChoices
                    .Where(c => c is not PendingDeliverEventChoice)
                    .Select(c => ComputeProgress(
                        c,
                        newChoices,
                        nextGuidanceIndex,
                        nextCondition,
                        newTraces,
                        recursionDepth + 1)).Max();

            if (choiceResultsInGoalEvent)
            {
                if (_guidance.FinalGuidanceIndex(guidanceIndex))
                {
                    // Progress computation has reached a violation, no need to
                    // recurse further
                    return 1;
                }

                // Even if prediction is unknown, an existing choice could produce
                // the next goal event, making more progress
                var maxFutureProgress = MaxFutureProgress(guidanceIndex + 1);

                // If maxFutureProgress is null, then all future paths result in 
                // invalidating the guidance, making this progress irrelevant.
                return maxFutureProgress == null ? null : 1 + maxFutureProgress;
            }
            else
            {
                if (EventHandledByMonitor(
                    candidateMachineChoice.GetMonitoredEvent(),
                    _guidance.GetCurrentMonitorState(guidanceIndex)))
                {
                    // candidateMachineChoice could invalidate the current guidance
                    return null;
                }

                // If prediction is unknown, recursing further will reveal no new
                // information. 
                return knownPrediction ? MaxFutureProgress(guidanceIndex) : 0;
            }
        }

        private void UpdateChoices(
            bool knownPrediction,
            IReadOnlyList<ExecutionEffect> prediction,
            StateMachineSchedulingChoice candidateChoice,
            List<SchedulingChoice> choices)
        {
            // JR TODO: Think about effect of complete vs incomplete predictions 

            if (candidateChoice is ResumeHandlerChoice ||
                candidateChoice is ResumeInitializationChoice)
            {
                if (knownPrediction)
                {
                    // Only remove resume choices if handler has completed; 
                    // otherwise, handler can be resumed again and it remains a 
                    // valid choice in the next step
                    bool candidateHandlerCompleted = prediction.Any(e =>
                        e is CompleteHandlerEffect &&
                        BehaviorStoreComparers.SameStateMachineId(
                            candidateChoice.StateMachineId,
                            e.StateMachineId));

                    if (candidateHandlerCompleted)
                        choices.Remove(candidateChoice);
                }
                else
                {
                    // If prediction is unknown, we don't know whether the handler 
                    // can be resumed again. To increase the likelihood of the
                    // progress computation eventually finishing, remove the choice.
                    choices.Remove(candidateChoice);
                }
            }
            else
            {
                choices.Remove(candidateChoice);
            }

            foreach (var effect in prediction)
            {
                UpdateChoicesFromEffect(effect, choices);
            }
        }

        private void UpdateChoicesFromEffect(
            ExecutionEffect effect,
            List<SchedulingChoice> choices)
        {
            // OpId is irrelevant for progress function's purposes
            ulong placeholderOpId = 0;

            switch (effect)
            {
                case SendEffect send:
                    var sentEvent = (send.SentEvent, new EventInfo(send.SentEvent));
                    switch (send.AvailableDeliveryType)
                    {
                        case SendEffect.DeliveryType.DeliverEvent:
                            choices.Add(new DeliverEventChoice(
                                placeholderOpId,
                                send.TargetStateMachineId,
                                sentEvent));
                            break;

                        case SendEffect.DeliveryType.CompleteReceive:
                            choices.Add(new CompleteReceiveChoice(
                                placeholderOpId,
                                send.TargetStateMachineId,
                                send.TargetInProgressEvent,
                                sentEvent));
                            break;

                        case SendEffect.DeliveryType.Pending:
                            choices.Add(new PendingDeliverEventChoice(
                                placeholderOpId,
                                send.TargetStateMachineId,
                                sentEvent));
                            break;
                    }
                    break;

                case CreateEffect create:
                    choices.Add(new InitializeChoice(
                        placeholderOpId,
                        create.CreatedStateMachineId,
                        create.InitialEvent));
                    break;

                case BlockOnReceiveEffect receive:
                {
                    var matchingChoices = choices.Where(c =>
                        c is PendingDeliverEventChoice pending &&
                        BehaviorStoreComparers.SameStateMachineId(
                            pending.StateMachineId,
                            receive.StateMachineId) &&
                        EventInbox.IsWaitedEvent(
                            pending.PendingEventToDeliver.e,
                            receive.ReceivePredicates.ToDictionary()));

                    var matchingPendingDeliveries =
                        ((IEnumerable<PendingDeliverEventChoice>)matchingChoices).ToList();

                    foreach (var matchingDelivery in matchingPendingDeliveries)
                    {
                        choices.Remove(matchingDelivery);
                    }

                    foreach (var matchingDelivery in matchingPendingDeliveries)
                    {
                        choices.Add(new CompleteReceiveChoice(
                            placeholderOpId,
                            receive.StateMachineId,
                            receive.InProgressEvent,
                            matchingDelivery.PendingEventToDeliver));
                    }
                    break;
                }

                case CompleteHandlerEffect complete:
                {
                    var matchingChoices = choices.Where(c =>
                    c is PendingDeliverEventChoice pending &&
                    BehaviorStoreComparers.SameStateMachineId(
                        pending.StateMachineId,
                        complete.StateMachineId));

                    var matchingPendingDeliveries =
                        ((IEnumerable<PendingDeliverEventChoice>)matchingChoices).ToList();

                    foreach (var matchingDelivery in matchingPendingDeliveries)
                    {
                        choices.Remove(matchingDelivery);
                    }

                    foreach (var matchingDelivery in matchingPendingDeliveries)
                    {
                        choices.Add(new DeliverEventChoice(
                            placeholderOpId,
                            matchingDelivery.StateMachineId,
                            matchingDelivery.PendingEventToDeliver));
                    }
                    break;
                }

                default:
                    // MonitorObservationEffect does not produce new choices
                    break;
            }
        }

        private bool ChoiceResultsInGoalEvent(
            SchedulingChoice choice,
            int guidanceIndex,
            SymExpr violationCondition,
            out SymExpr nextCondition)
        {
            nextCondition = violationCondition;
            switch (choice)
            {
                case StateMachineSchedulingChoice machineChoice:
                    var e = machineChoice.GetMonitoredEvent();
                    if (e == null)
                        return false;

                    return IsGoalEvent(
                        e,
                        guidanceIndex,
                        violationCondition,
                        out nextCondition);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Checks if e is a valid goal event according to the given guidanceIndex
        /// and violationCondition. 
        /// </summary>
        /// <param name="e"></param>
        /// <param name="guidanceIndex"></param>
        /// <param name="violationCondition"></param>
        /// <param name="nextCondition">
        /// If true is returned, updated condition with equality constraint 
        /// requiring event at goalIndex to be e. 
        /// If false is returned, equal to currentCondition.
        /// </param>
        /// <returns>
        /// True if e is confirmed to be a valid goal event, false if not (this 
        /// includes an Unknown result from the solver).
        /// </returns>
        private bool IsGoalEvent(
            RuntimeEvent e, 
            int guidanceIndex,
            SymExpr violationCondition,
            out SymExpr nextCondition)
        {
            nextCondition = violationCondition;
            SymEvent goalEvent = _guidance.GetNextViolatingEvent(guidanceIndex);

            if (!EventTypeMatches(goalEvent.Event, e))
                return false;

            var concretePayload = new ConcreteExpr(
                e.Payload?.Clone(),
                goalEvent.Payload.Type);

            var candidateCondition = SymExprFactory.And(
                violationCondition,
                SymExprFactory.Equal(goalEvent.Payload, concretePayload));

            if (_solver.CheckSat(candidateCondition) == SatResult.Sat)
            {
                nextCondition = candidateCondition;
                return true;
            }

            return false;
        }

        public static bool EventTypeMatches(
            ASTEvent expected,
            RuntimeEvent actual)
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

        /// <summary>
        /// Returns whether the given event is handled by the monitor in 
        /// monitorState. For an event to be handled, it must be both observed 
        /// by the monitor and not be ignored in monitorState.
        /// </summary>
        private bool EventHandledByMonitor(RuntimeEvent e, ASTState monitorState)
        {
            if (e == null ||
                !_monitorAnalyzer.IsEventObserved(e))
                return false;

            return monitorState.AllEventHandlers.Any(handler =>
                EventTypeMatches(handler.Key, e) &&
                handler.Value is not EventIgnore);
        }

        /// <inheritdoc/>
        public virtual bool GetNextBooleanChoice(
            SchedulingChoice lastChoice, 
            int maxValue, 
            out bool next)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public virtual bool GetNextIntegerChoice(
            SchedulingChoice lastChoice, 
            int maxValue, 
            out int next)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public virtual bool PrepareForNextIteration()
        {
            _monitor = null;
            _monitorAnalyzer = null;
            _machineTraces.Clear();
            _guidance = null;
            return true;
        }

        /// <inheritdoc/>
        public virtual int GetScheduledSteps()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public virtual bool HasReachedMaxSchedulingSteps()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public virtual bool IsFair()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public virtual string GetDescription()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public virtual void Reset()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public virtual void NotifyEffects(
            SchedulingChoice lastChoice,
            IReadOnlyList<ExecutionEffect> effects, 
            bool completeBehavior)
        {
            // Check for MonitorObservationEffects and update guidance as necessary.
            if (_guidance != null)
            {
                foreach (var effect in effects)
                {
                    if (effect is MonitorObservationEffect observation)
                    {
                        // Break early if observation invalidates guidance
                        if (!UpdateGuidance(observation))
                            break;
                    }
                }
            }

            var creationPath = lastChoice.GetCreationPath();
            if (creationPath == null) return;

            if (!_machineTraces.TryGetValue(creationPath, out var machineTrace))
            {
                machineTrace = new();
                _machineTraces[creationPath] = machineTrace;
            }

            _predictor.AddObservation(
                creationPath,
                machineTrace,
                lastChoice,
                effects,
                completeBehavior);
            machineTrace.Add(lastChoice);
        }

        /// <summary>
        /// Updates the current monitor guidance according to the given observation.
        /// </summary>
        /// <returns>
        /// True if guidance is still valid, false if it was invalidated by observation.
        /// </returns>
        private bool UpdateGuidance(MonitorObservationEffect observation)
        {
            if (IsGoalEvent(
                observation.ObservedEvent,
                _guidanceIndex,
                _guidance.ViolationCondition,
                out var newCondition))
            {
                _guidanceIndex++;
                _guidance.ViolationCondition = newCondition;
                return true;
            }

            // Ignored observations must still be recorded because they may not
            // be ignored in other monitor states
            if (observation.Ignored)
                return true;

            // If the observed event is not a goal event and not ignored, then it
            // invalidates the current guidance
            _guidance = null;
            return false;
        }

        /// <summary>
        /// Special scheduling choice used only by the progress function; represents
        /// a sent event that cannot be received by the target yet. Once the target
        /// is ready to receive it, the choice will become a DeliverEventChoice 
        /// or a CompleteReceiveChoice.
        /// </summary>
        private class PendingDeliverEventChoice : StateMachineSchedulingChoice
        {
            /// <summary>
            /// Pending event that may eventually be delivered to the operation.
            /// </summary>
            public (RuntimeEvent e, EventInfo info) PendingEventToDeliver { get; }

            internal PendingDeliverEventChoice(
            ulong operationId,
            StateMachineId targetMachineId,
            (RuntimeEvent e, EventInfo info) eventToDeliver)
            : base(operationId, targetMachineId)
            {
                PendingEventToDeliver = eventToDeliver;
            }
        }
    }
}
