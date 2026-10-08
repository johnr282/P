using Antlr4.Runtime.Atn;
using LanguageExt.Pipes;
using Microsoft.Z3;
using PChecker.IO.Debugging;
using PChecker.Random;
using PChecker.Runtime.Events;
using PChecker.Runtime.Exceptions;
using PChecker.Runtime.Specifications;
using PChecker.Runtime.StateMachines;
using PChecker.Runtime.StateMachines.EventInboxes;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Strategies.MonitorGuided.Predictors;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution;
using PChecker.SystematicTesting.Strategies.MonitorGuided.SymbolicExecution.Solver;
using Plang.Compiler.TypeChecker.AST.Declarations;
using Plang.Compiler.TypeChecker.AST.States;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ASTEvent = Plang.Compiler.TypeChecker.AST.Declarations.Event;
using ASTState = Plang.Compiler.TypeChecker.AST.States.State;
using RuntimeEvent = PChecker.Runtime.Events.Event;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    internal class MonitorGuidedStrategy : ISchedulingStrategy
    {
        private readonly uint maxObservedEventsForMonitorExploration;
        private readonly uint maxRecursionDepthForProgressComputation;

        // For now, assume that there is only one monitor.
        private Monitor _monitor;
        private MonitorAnalyzer _monitorAnalyzer;

        private readonly Dictionary<MachineCreationPath, MachineState> 
            _machineStates = new();

        private readonly IPredictor _predictor;

        private readonly ISolver _solver;

        private MonitorGuidance _guidance;

        private readonly IRandomValueGenerator _random;

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

            if (_guidance == null || !_guidance.Valid)
            {
                bool monitorViolationFound = _monitorAnalyzer.GetMonitorGuidance(
                    _monitor.CurrentStateName,
                    out _guidance);

                if (!monitorViolationFound)
                {
                    bool result = GetFallbackChoice(lastChoice, choices, out next);
                    UpdateStatesFromChoice(next, _machineStates);
                    return result;
                }
            }

            int? maxProgressValue = null;
            List<SchedulingChoice> candidateChoices = new();
            foreach (var choice in choices)
            {
                int? progressValue = ComputeProgress(
                    choice,
                    _guidance,
                    _machineStates,
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

                bool result = GetFallbackChoice(lastChoice, choices, out next);
                UpdateStatesFromChoice(next, _machineStates);
                return result;
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
            UpdateStatesFromChoice(next, _machineStates);
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
            MonitorGuidance guidance,
            Dictionary<MachineCreationPath, MachineState> machineStates,
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
            var candidateState = GetStateOrThrow(candidatePath, machineStates);

            bool knownPrediction = _predictor.PredictEffects(
                candidatePath,
                candidateState.Trace,
                candidateMachineChoice,
                out var prediction);

            var newStates = CloneMachineStates(machineStates);
            var newGuidance = guidance.Clone();
            int startingGuidanceIndex = newGuidance.GuidanceIndex;

            UpdateStatesAndGuidance(
                candidateMachineChoice, 
                knownPrediction, 
                prediction, 
                newStates,
                newGuidance);

            int? progressFromCandidate = newGuidance.Valid
                ? newGuidance.GuidanceIndex - startingGuidanceIndex : null;

            if (progressFromCandidate == null ||
                newGuidance.ViolationReached ||
                (progressFromCandidate == 0 && !knownPrediction))
            {
                /* 3 cases result in halting:
                 * 1: candidateChoice is predicted to invalidate guidance.
                 * 2: candidateChoice is predicted to reach a violation, so no 
                   further progress is possible.
                 * 3: Prediction for candidateChoice is unknown, so further 
                   predictions will also be unknown. Guidance was not updated, 
                   so no existing choices can become goal events. Therefore, 
                   further progress computations will have no new opportunities 
                   to make progress.
                 */
                return progressFromCandidate;
            }

            var newChoices = ConstructChoices(newStates);
            var maxFutureProgress = newChoices.Select(c => ComputeProgress(
                c,
                newGuidance,
                newStates,
                recursionDepth + 1)).Max();

            // If maxFutureProgress is null, then all future paths result in 
            // invalidating the guidance, making this progress irrelevant.
            return maxFutureProgress == null 
                ? null : progressFromCandidate + maxFutureProgress;
        }

        private void UpdateStatesAndGuidance(
            StateMachineSchedulingChoice choice,
            bool knownPrediction,
            IReadOnlyList<ExecutionEffect> prediction,
            Dictionary<MachineCreationPath, MachineState> machineStates,
            MonitorGuidance guidance)
        {
            UpdateStatesFromChoice(choice, machineStates);
            UpdateGuidanceFromChoice(choice, guidance);

            if (!knownPrediction) return;

            var filteredPrediction = prediction.ToList();

            if (choice is DeliverEventChoice ||
                choice is CompleteReceiveChoice)
            {
                // Both choice types result in a MonitorObservationEffect when 
                // their event is delivered; to prevent a single event delivery
                // from updating guidance twice, remove this effect from prediction
                var repeatedObservation = prediction.FirstOrDefault(effect =>
                    effect is MonitorObservationEffect observation &&
                    BehaviorStoreComparers.SameEvent(
                        observation.ObservedEvent, choice.GetMonitoredEvent()));
                filteredPrediction.Remove(repeatedObservation);
            }

            foreach (var effect in filteredPrediction)
            {
                UpdateStatesAndGuidanceFromEffect(effect, machineStates, guidance);
            }
        }

        /// <summary>
        /// Construct all available state machine choices from the given machine
        /// states. 
        /// </summary>
        private List<SchedulingChoice> ConstructChoices(
            IReadOnlyDictionary<MachineCreationPath, MachineState> machineStates)
        {
            // JR TODO: Figure out a way to deduplicate logic from here and 
            // OperationScheduler.GetSchedulingChoices().

            // OpId is irrelevant for progress function's purposes
            ulong placeholderOpId = 0;

            var choices = new List<SchedulingChoice>();
            foreach (var (path, state) in machineStates)
            {
                switch (state.CurrentStatus)
                {
                    case MachineState.Status.InitializationPending:
                        choices.Add(new InitializeChoice(
                            placeholderOpId, state.Id, state.InitialEvent));
                        break;

                    case MachineState.Status.Initializing:
                        choices.Add(new ResumeInitializationChoice(
                            placeholderOpId, state.Id, state.InitialEvent));
                        break;

                    case MachineState.Status.HandlingEvent:
                        choices.Add(new ResumeHandlerChoice(
                            placeholderOpId, state.Id, state.InProgressEvent));
                        break;

                    case MachineState.Status.BlockedOnReceive:
                        foreach (var ev in state.Inbox)
                        {
                            if (EventInbox.IsWaitedEvent(
                                ev.e, state.ReceivePredicates))
                            {
                                choices.Add(new CompleteReceiveChoice(
                                    placeholderOpId,
                                    state.Id,
                                    state.InProgressEvent,
                                    ev));
                            }
                        }
                        break;

                    case MachineState.Status.Idle:
                        foreach (var ev in state.Inbox)
                        {
                            choices.Add(new DeliverEventChoice(
                                placeholderOpId, state.Id, ev));
                        }
                        break;

                    case MachineState.Status.Halted:
                        // Cannot be scheduled, so no choices
                        break;
                }
            }

            return choices;
        }

        private void UpdateGuidanceFromChoice(StateMachineSchedulingChoice choice,
            MonitorGuidance guidance)
        {
            var e = choice.GetMonitoredEvent();
            if (e == null)
                return;

            UpdateGuidanceFromEvent(e, guidance);
        }

        /// <summary>
        /// Updates the given guidance according to e.
        /// </summary>
        private void UpdateGuidanceFromEvent(RuntimeEvent e, MonitorGuidance guidance)
        {
            if (guidance.ViolationReached ||
                !EventHandledByMonitor(e, guidance.GetCurrentMonitorState()))
                return;

            // e will be handled by the monitor, so in order to not invalidate
            // guidance, it must be a goal event
            
            SymEvent goalEvent = guidance.GetNextGoalEvent();

            var concretePayload = new ConcreteExpr(
                e.Payload?.Clone(),
                goalEvent.Payload.Type);

            var candidateCondition = SymExprFactory.And(
                guidance.ViolationCondition,
                SymExprFactory.Equal(goalEvent.Payload, concretePayload));

            bool isGoalEvent = EventTypeMatches(goalEvent.Event, e) &&
                _solver.CheckSat(candidateCondition) == SatResult.Sat;

            if (isGoalEvent)
            {
                guidance.GuidanceIndex++;
                guidance.ViolationCondition = candidateCondition;
                return;
            }

            guidance.Valid = false;
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
            _machineStates.Clear();
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
            foreach (var effect in effects)
            {
                UpdateStatesAndGuidanceFromEffect(effect, _machineStates, _guidance);
            }

            var creationPath = lastChoice.GetCreationPath();
            if (creationPath == null) return;

            var machineState = GetStateOrThrow(creationPath, _machineStates);

            // lastChoice is already added to machine trace, so don't pass full
            // trace to predictor
            _predictor.AddObservation(
                creationPath,
                machineState.Trace[0..^1],
                lastChoice,
                effects,
                completeBehavior);
        }

        private void UpdateStatesAndGuidanceFromEffect(
            ExecutionEffect effect,
            Dictionary<MachineCreationPath, MachineState> machineStates,
            MonitorGuidance guidance)
        {
            switch (effect)
            {
                case SendEffect send:
                    {
                        var targetState = GetStateOrThrow(
                            send.TargetStateMachineId.CreationPath, machineStates);
                        targetState.OnSentEvent(send.SentEvent);
                        break;
                    }
                case MonitorObservationEffect observation:
                    UpdateGuidanceFromObservation(observation, guidance);
                    break;

                case CreateEffect create:
                    {
                        var createdPath = create.CreatedStateMachineId.CreationPath;
                        if (machineStates.ContainsKey(createdPath))
                        {
                            throw new PInternalException(
                                "Newly created machine already has a machine state");
                        }

                        machineStates[createdPath] = new MachineState(
                            create.CreatedStateMachineId, create.InitialEvent);
                        break;
                    }
                case BlockOnReceiveEffect receive:
                    {
                        var blockedState = GetStateOrThrow(
                           effect.StateMachineId.CreationPath, machineStates);
                        blockedState.OnReceive(receive.ReceivePredicates.ToDictionary());
                        break;
                    }

                case CompleteHandlerEffect complete:
                    {
                        var completedState = GetStateOrThrow(
                           effect.StateMachineId.CreationPath, machineStates);
                        completedState.OnCompleteHandler();
                        break;
                    }

                case HaltEffect haltEffect:
                    {
                        var haltedState = GetStateOrThrow(
                           effect.StateMachineId.CreationPath, machineStates);
                        haltedState.OnHalt();
                        break;
                    }
                    
                default:
                    throw new NotSupportedException(
                        $"Unsupported effect type {effect.GetType().Name}");
            }
        }

        private static void UpdateStatesFromChoice(SchedulingChoice choice,
            Dictionary<MachineCreationPath, MachineState> machineStates)
        {
            var machinePath = choice.GetCreationPath();
            if (machinePath == null) return;

            var state = GetStateOrThrow(machinePath, machineStates);
            state.Trace.Add(choice);

            switch (choice)
            {
                case InitializeChoice init:
                    state.OnInit(init.InitialEvent);
                    break;

                case DeliverEventChoice deliver:
                    state.OnEventDelivered(deliver.EventToDeliver);
                    break;

                case CompleteReceiveChoice receive:
                    state.OnReceiveCompletion(receive.EventToDeliver);
                    break;

                case ResumeInitializationChoice:
                case ResumeHandlerChoice:
                    break;

                default:
                    throw new NotSupportedException(
                        $"Unsupported scheduling choice type {choice.GetType().Name}");
            }
        }

        private static MachineState GetStateOrThrow(MachineCreationPath path,
            Dictionary<MachineCreationPath, MachineState> machineStates)
        {
            if (!machineStates.TryGetValue(path, out var state))
            {
                throw new PInternalException("Machine state not found");
            }
            return state;
        }

        private static Dictionary<MachineCreationPath, MachineState> CloneMachineStates(
            IReadOnlyDictionary<MachineCreationPath, MachineState> machineStates)
        {
            return machineStates.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone());
        }

        /// <summary>
        /// Updates the given monitor guidance according to the given observation.
        /// </summary>
        private void UpdateGuidanceFromObservation(
            MonitorObservationEffect observation,
            MonitorGuidance guidance)
        {
            if (!guidance.Valid || observation.Ignored) return;

            UpdateGuidanceFromEvent(observation.ObservedEvent, guidance);
        }

        private class MachineState
        {
            public StateMachineId Id { get; }
            public RuntimeEvent InitialEvent { get; }
            public List<SchedulingChoice> Trace { get; } = new();
            public Status CurrentStatus { get; set; } = Status.InitializationPending;
            public (RuntimeEvent e, EventInfo info) InProgressEvent { get; set; } 
                = (null, null);

            public Dictionary<Type, Func<RuntimeEvent, bool>> 
                ReceivePredicates { get; set; } = null;

            // JR TODO: Assuming event set inbox type for now; will need to change this
            public HashSet<(RuntimeEvent e, EventInfo info)> Inbox { get; } = new();

            public enum Status
            {
                InitializationPending, 
                Initializing, 
                HandlingEvent,
                BlockedOnReceive,
                Idle,
                Halted
            }

            public MachineState(StateMachineId id, RuntimeEvent initialEvent)
            {
                Id = id;
                InitialEvent = initialEvent;
            }

            public MachineState Clone()
            {
                var clone = new MachineState(Id, InitialEvent?.Snapshot())
                {
                    CurrentStatus = CurrentStatus,
                    InProgressEvent = CloneEventWithMetadata(InProgressEvent),
                    ReceivePredicates = ReceivePredicates == null
                        ? null
                        : new Dictionary<Type, Func<RuntimeEvent, bool>>(ReceivePredicates)
                };

                clone.Trace.AddRange(Trace.Select(choice => choice.Snapshot()));
                foreach (var ev in Inbox)
                {
                    clone.Inbox.Add(CloneEventWithMetadata(ev));
                }

                return clone;
            }

            private static (RuntimeEvent e, EventInfo info) CloneEventWithMetadata(
                (RuntimeEvent e, EventInfo info) ev)
            {
                var clonedEvent = ev.e?.Snapshot();
                EventInfo clonedInfo = null;

                if (ev.info != null)
                {
                    if (clonedEvent == null)
                    {
                        throw new PInternalException(
                            "Cannot clone event metadata without its event");
                    }

                    var origin = ev.info.OriginInfo;
                    clonedInfo = origin == null
                        ? new EventInfo(clonedEvent)
                        : new EventInfo(
                            clonedEvent,
                            new EventOriginInfo(
                                origin.SenderStateMachineId,
                                origin.SenderStateMachineName,
                                origin.SenderStateName),
                            ev.info.VectorTime);
                }

                return (clonedEvent, clonedInfo);
            }

            public void OnSentEvent((RuntimeEvent e, EventInfo) ev)
            {
                Inbox.Add(ev);
            }

            public void OnReceive(Dictionary<Type, Func<RuntimeEvent, bool>> receivePredicates)
            {
                CurrentStatus = Status.BlockedOnReceive;
                ReceivePredicates = receivePredicates;
            }

            public void OnCompleteHandler()
            {
                if (CurrentStatus != Status.Halted)
                {
                    CurrentStatus = Status.Idle;
                }
                InProgressEvent = (null, null);
            }

            public void OnHalt()
            {
                CurrentStatus = Status.Halted;
            }

            public void OnInit(RuntimeEvent initialEvent)
            {
                CurrentStatus = Status.Initializing;
                InProgressEvent = (initialEvent, null);
            }

            public void OnEventDelivered((RuntimeEvent e, EventInfo info) ev)
            {
                if (CurrentStatus == Status.Halted) return;

                RemoveOrThrow(ev);
                CurrentStatus = Status.HandlingEvent;
                InProgressEvent = ev;
            }

            public void OnReceiveCompletion((RuntimeEvent e, EventInfo info) ev)
            {
                RemoveOrThrow(ev);
                CurrentStatus = Status.HandlingEvent;
                ReceivePredicates = null;
            }

            private void RemoveOrThrow((RuntimeEvent e, EventInfo info) ev)
            {
                if (!Inbox.Remove(ev))
                {
                    throw new PInternalException("Event to remove not in inbox");
                }
            }
        }
    }
}
