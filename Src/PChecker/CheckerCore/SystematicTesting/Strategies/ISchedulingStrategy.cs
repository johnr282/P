// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using PChecker.SystematicTesting.Operations;

namespace PChecker.SystematicTesting.Strategies
{
    /// <summary>
    /// Interface of an exploration strategy used during controlled testing.
    /// </summary>
    internal interface ISchedulingStrategy
    {
        /// <summary>
        /// Returns the next scheduling choice.
        /// </summary>
        /// <param name="lastChoice">The most recent scheduling choice.</param>
        /// <param name="choices">List of possible scheduling choices.</param>
        /// <param name="next">The chosen next scheduling choice.</param>
        /// <returns>True if there is a next choice, else false.</returns>
        bool GetNextSchedulingChoice(
            SchedulingChoice lastChoice, 
            IEnumerable<SchedulingChoice> choices, 
            out SchedulingChoice next);

        /// <summary>
        /// Returns the next boolean choice.
        /// </summary>
        /// <param name="lastChoice">The most recent scheduling choice.</param>
        /// <param name="maxValue">The max value.</param>
        /// <param name="next">The next boolean choice.</param>
        /// <returns>True if there is a next choice, else false.</returns>
        bool GetNextBooleanChoice(SchedulingChoice lastChoice, int maxValue, out bool next);

        /// <summary>
        /// Returns the next integer choice.
        /// </summary>
        /// <param name="lastChoice">The most recent scheduling choice.</param>
        /// <param name="maxValue">The max value.</param>
        /// <param name="next">The next integer choice.</param>
        /// <returns>True if there is a next choice, else false.</returns>
        bool GetNextIntegerChoice(SchedulingChoice lastChoice, int maxValue, out int next);

        /// <summary>
        /// Prepares for the next schedule. This is invoked
        /// at the end of a schedule. It must return false
        /// if the scheduling strategy should stop exploring.
        /// </summary>
        /// <returns>True to start the next schedule.</returns>
        bool PrepareForNextIteration();

        /// <summary>
        /// Returns the scheduled steps.
        /// </summary>
        int GetScheduledSteps();

        /// <summary>
        /// True if the scheduling strategy has reached the max
        /// scheduling steps for the given schedule.
        /// </summary>
        bool HasReachedMaxSchedulingSteps();

        /// <summary>
        /// Checks if this is a fair scheduling strategy.
        /// </summary>
        bool IsFair();

        /// <summary>
        /// Returns a textual description of the scheduling strategy.
        /// </summary>
        string GetDescription();

        /// <summary>
        /// Resets the scheduling strategy. This is typically invoked by
        /// parent strategies to reset child strategies.
        /// </summary>
        void Reset();
    }
}