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

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    internal class MonitorGuidedStrategy : ISchedulingStrategy
    {
        private readonly uint maxObservedEventsForMonitorExploration;

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
                // JR TODO: Should RunTaskChoices get special handling? Computing
                // progress does not really make sense for them, so they would 
                // always have a null progress value. However, then they might
                // never be chosen, which could cause issues
                int? progressValue = ComputeProgress(
                    choice,
                    _guidance,
                    _guidanceIndex,
                    _guidance.ViolationCondition,
                    _machineTraces,
                    recursionDepth: 0);

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
            MonitorGuidance guidance,
            int guidanceIndex,
            SymExpr violationCondition,
            Dictionary<MachineCreationPath, List<SchedulingChoice>> machineTraces,
            int recursionDepth)
        {
            // JR TODO
            throw new NotImplementedException();
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
            SymEvent goalEvent = _guidance.ViolatingExecution[guidanceIndex];

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

        private static bool EventTypeMatches(
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
    }
}
