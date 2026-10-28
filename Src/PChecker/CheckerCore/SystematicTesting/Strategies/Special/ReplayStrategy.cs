// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using PChecker.Configuration;
using PChecker.IO.Debugging;
using PChecker.SystematicTesting.Operations;
using PChecker.SystematicTesting.Traces;

namespace PChecker.SystematicTesting.Strategies.Special
{
    /// <summary>
    /// Class representing a replaying scheduling strategy.
    /// </summary>
    internal sealed class ReplayStrategy : ISchedulingStrategy
    {
        /// <summary>
        /// The checkerConfiguration.
        /// </summary>
        private readonly CheckerConfiguration _checkerConfiguration;

        /// <summary>
        /// The program schedule trace.
        /// </summary>
        private readonly ScheduleTrace ScheduleTrace;

        /// <summary>
        /// The suffix strategy.
        /// </summary>
        private readonly ISchedulingStrategy SuffixStrategy;

        /// <summary>
        /// Is the scheduler that produced the
        /// schedule trace fair?
        /// </summary>
        private readonly bool IsSchedulerFair;

        /// <summary>
        /// Is the scheduler replaying the trace?
        /// </summary>
        private bool IsReplaying;

        /// <summary>
        /// The number of scheduled steps.
        /// </summary>
        private int ScheduledSteps;

        /// <summary>
        /// Text describing a replay error.
        /// </summary>
        internal string ErrorText { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReplayStrategy"/> class.
        /// </summary>
        public ReplayStrategy(CheckerConfiguration checkerConfiguration, ScheduleTrace trace, bool isFair)
            : this(checkerConfiguration, trace, isFair, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReplayStrategy"/> class.
        /// </summary>
        public ReplayStrategy(CheckerConfiguration checkerConfiguration, ScheduleTrace trace, bool isFair, ISchedulingStrategy suffixStrategy)
        {
            _checkerConfiguration = checkerConfiguration;
            ScheduleTrace = trace;
            ScheduledSteps = 0;
            IsSchedulerFair = isFair;
            IsReplaying = true;
            SuffixStrategy = suffixStrategy;
            ErrorText = string.Empty;
        }

        /// <inheritdoc/>
        public bool GetNextSchedulingChoice(
            SchedulingChoice lastChoice,
            IEnumerable<SchedulingChoice> choices,
            out SchedulingChoice next)
        {
            if (IsReplaying)
            {
                try
                {
                    if (ScheduledSteps >= ScheduleTrace.Count)
                    {
                        ErrorText = "Trace is not reproducible: execution is longer than trace.";
                        throw new InvalidOperationException(ErrorText);
                    }

                    var nextStep = ScheduleTrace[ScheduledSteps];
                    if (nextStep.Type != ScheduleStepType.SchedulingChoice)
                    {
                        ErrorText = "Trace is not reproducible: next step is not a scheduling choice.";
                        throw new InvalidOperationException(ErrorText);
                    }

                    next = choices.FirstOrDefault(choice =>
                        choice.OperationId == nextStep.ScheduledOperationId &&
                        choice.ChoiceId == nextStep.ChoiceId);
                    if (next is null)
                    {
                        ErrorText = $"Trace is not reproducible: cannot detect choice '{nextStep.ScheduledOperationId}|{nextStep.ChoiceId}'.";
                        throw new InvalidOperationException(ErrorText);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    if (SuffixStrategy is null)
                    {
                        if (!_checkerConfiguration.DisableEnvironmentExit)
                        {
                            Error.ReportAndExit(ex.Message);
                        }

                        next = null;
                        return false;
                    }

                    IsReplaying = false;
                    return SuffixStrategy.GetNextSchedulingChoice(lastChoice, choices, out next);
                }

                ScheduledSteps++;
                return true;
            }

            return SuffixStrategy.GetNextSchedulingChoice(lastChoice, choices, out next);
        }

        /// <inheritdoc/>
        public bool GetNextBooleanChoice(SchedulingChoice lastChoice, int maxValue, out bool next)
        {
            if (IsReplaying)
            {
                ScheduleStep nextStep;

                try
                {
                    if (ScheduledSteps >= ScheduleTrace.Count)
                    {
                        ErrorText = "Trace is not reproducible: execution is longer than trace.";
                        throw new InvalidOperationException(ErrorText);
                    }

                    nextStep = ScheduleTrace[ScheduledSteps];
                    if (nextStep.Type != ScheduleStepType.NondeterministicChoice)
                    {
                        ErrorText = "Trace is not reproducible: next step is not a nondeterministic choice.";
                        throw new InvalidOperationException(ErrorText);
                    }

                    if (nextStep.BooleanChoice is null)
                    {
                        ErrorText = "Trace is not reproducible: next step is not a nondeterministic boolean choice.";
                        throw new InvalidOperationException(ErrorText);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    if (SuffixStrategy is null)
                    {
                        if (!_checkerConfiguration.DisableEnvironmentExit)
                        {
                            Error.ReportAndExit(ex.Message);
                        }

                        next = false;
                        return false;
                    }

                    IsReplaying = false;
                    return SuffixStrategy.GetNextBooleanChoice(lastChoice, maxValue, out next);
                }

                next = nextStep.BooleanChoice.Value;
                ScheduledSteps++;
                return true;
            }

            return SuffixStrategy.GetNextBooleanChoice(lastChoice, maxValue, out next);
        }

        /// <inheritdoc/>
        public bool GetNextIntegerChoice(SchedulingChoice lastChoice, int maxValue, out int next)
        {
            if (IsReplaying)
            {
                ScheduleStep nextStep;

                try
                {
                    if (ScheduledSteps >= ScheduleTrace.Count)
                    {
                        ErrorText = "Trace is not reproducible: execution is longer than trace.";
                        throw new InvalidOperationException(ErrorText);
                    }

                    nextStep = ScheduleTrace[ScheduledSteps];
                    if (nextStep.Type != ScheduleStepType.NondeterministicChoice)
                    {
                        ErrorText = "Trace is not reproducible: next step is not a nondeterministic choice.";
                        throw new InvalidOperationException(ErrorText);
                    }

                    if (nextStep.IntegerChoice is null)
                    {
                        ErrorText = "Trace is not reproducible: next step is not a nondeterministic integer choice.";
                        throw new InvalidOperationException(ErrorText);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    if (SuffixStrategy is null)
                    {
                        if (!_checkerConfiguration.DisableEnvironmentExit)
                        {
                            Error.ReportAndExit(ex.Message);
                        }

                        next = 0;
                        return false;
                    }

                    IsReplaying = false;
                    return SuffixStrategy.GetNextIntegerChoice(lastChoice, maxValue, out next);
                }

                next = nextStep.IntegerChoice.Value;
                ScheduledSteps++;
                return true;
            }

            return SuffixStrategy.GetNextIntegerChoice(lastChoice, maxValue, out next);
        }

        /// <inheritdoc/>
        public bool PrepareForNextIteration()
        {
            ScheduledSteps = 0;
            if (SuffixStrategy != null)
            {
                return SuffixStrategy.PrepareForNextIteration();
            }

            return false;
        }

        /// <inheritdoc/>
        public int GetScheduledSteps()
        {
            if (SuffixStrategy != null)
            {
                return ScheduledSteps + SuffixStrategy.GetScheduledSteps();
            }

            return ScheduledSteps;
        }

        /// <inheritdoc/>
        public bool HasReachedMaxSchedulingSteps()
        {
            if (SuffixStrategy != null)
            {
                return SuffixStrategy.HasReachedMaxSchedulingSteps();
            }

            return false;
        }

        /// <inheritdoc/>
        public bool IsFair()
        {
            if (SuffixStrategy != null)
            {
                return SuffixStrategy.IsFair();
            }

            return IsSchedulerFair;
        }

        /// <inheritdoc/>
        public void NotifyEffects(
            SchedulingChoice lastChoice,
            IReadOnlyList<ExecutionEffect> effects,
            bool completeBehavior)
        {
            // Replay does not use execution observations.
        }

        /// <inheritdoc/>
        public string GetDescription()
        {
            if (SuffixStrategy != null)
            {
                return "replay(" + SuffixStrategy.GetDescription() + ")";
            }

            return "replay";
        }

        /// <inheritdoc/>
        public void Reset()
        {
            ScheduledSteps = 0;
            SuffixStrategy?.Reset();
        }
    }
}
