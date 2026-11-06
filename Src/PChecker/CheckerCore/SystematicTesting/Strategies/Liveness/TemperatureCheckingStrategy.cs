// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using PChecker.Configuration;
using PChecker.Runtime.Specifications;
using PChecker.SystematicTesting.Operations;

namespace PChecker.SystematicTesting.Strategies.Liveness
{
    /// <summary>
    /// Strategy for detecting liveness property violations using the "temperature"
    /// method. It contains a nested <see cref="ISchedulingStrategy"/> that is used
    /// for scheduling decisions. Note that liveness property violations are checked
    /// only if the nested strategy is fair.
    /// </summary>
    internal sealed class TemperatureCheckingStrategy : LivenessCheckingStrategy
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TemperatureCheckingStrategy"/> class.
        /// </summary>
        internal TemperatureCheckingStrategy(CheckerConfiguration checkerConfiguration, List<Monitor> monitors, ISchedulingStrategy strategy)
            : base(checkerConfiguration, monitors, strategy)
        {
        }

        /// <inheritdoc/>
        public override bool GetNextSchedulingChoice(
            SchedulingChoice lastChoice, IEnumerable<SchedulingChoice> choices, out SchedulingChoice next)
        {
            CheckLivenessTemperature();
            return SchedulingStrategy.GetNextSchedulingChoice(lastChoice, choices, out next);
        }

        /// <inheritdoc/>
        public override bool GetNextBooleanChoice(SchedulingChoice lastChoice, int maxValue, out bool next)
        {
            CheckLivenessTemperature();
            return SchedulingStrategy.GetNextBooleanChoice(lastChoice, maxValue, out next);
        }

        /// <inheritdoc/>
        public override bool GetNextIntegerChoice(SchedulingChoice lastChoice, int maxValue, out int next)
        {
            CheckLivenessTemperature();
            return SchedulingStrategy.GetNextIntegerChoice(lastChoice, maxValue, out next);
        }

        /// <summary>
        /// Checks the liveness temperature of each monitor, and
        /// reports an error if one of the liveness monitors has
        /// passed the temperature threshold.
        /// </summary>
        private void CheckLivenessTemperature()
        {
            if (IsFair())
            {
                foreach (var monitor in Monitors)
                {
                    monitor.CheckLivenessTemperature();
                }
            }
        }
    }
}
