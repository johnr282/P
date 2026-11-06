// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using PChecker.Random;
using PChecker.SystematicTesting.Operations;

namespace PChecker.SystematicTesting.Strategies.Probabilistic
{
    /// <summary>
    /// A randomized scheduling strategy with increased probability
    /// to remain in the same scheduling choice.
    /// </summary>
    internal sealed class ProbabilisticRandomStrategy : RandomStrategy
    {
        /// <summary>
        /// Number of coin flips.
        /// </summary>
        private readonly int NumberOfCoinFlips;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProbabilisticRandomStrategy"/> class.
        /// </summary>
        public ProbabilisticRandomStrategy(int maxSteps, int numberOfCoinFlips, IRandomValueGenerator random)
            : base(maxSteps, random)
        {
            NumberOfCoinFlips = numberOfCoinFlips;
        }

        /// <inheritdoc/>
        public override bool GetNextSchedulingChoice(
            SchedulingChoice lastChoice, IEnumerable<SchedulingChoice> choices, out SchedulingChoice next)
        {
            var operations = choices.GroupBy(choice => choice.OperationId).ToList();
            IGrouping<ulong, SchedulingChoice> operation = null;
            if (operations.Count > 1 && !ShouldCurrentMachineChange())
            {
                operation = operations.Find(group => group.Key == lastChoice.OperationId);
            }

            operation ??= operations[RandomValueGenerator.Next(operations.Count)];
            var choiceCount = operation.Count();
            var choiceIndex = choiceCount == 1 ? 0 : RandomValueGenerator.Next(choiceCount);
            next = operation.ElementAt(choiceIndex);
            ScheduledSteps++;
            return true;
        }

        /// <inheritdoc/>
        public override string GetDescription() =>
            $"probabilistic[seed '{RandomValueGenerator.Seed}', coin flips '{NumberOfCoinFlips}']";

        /// <summary>
        /// Flip the coin a specified number of times.
        /// </summary>
        private bool ShouldCurrentMachineChange()
        {
            for (var idx = 0; idx < NumberOfCoinFlips; idx++)
            {
                if (RandomValueGenerator.Next(2) == 1)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
