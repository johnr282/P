// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using PChecker.Generator.Object;
using PChecker.IO.Debugging;
using PChecker.Random;
using PChecker.SystematicTesting.Operations;

namespace PChecker.SystematicTesting.Strategies.Probabilistic
{
    /// <summary>
    /// A priority-based probabilistic scheduling strategy.
    /// </summary>
    /// <remarks>
    /// This strategy is described in the following paper:
    /// https://www.microsoft.com/en-us/research/wp-content/uploads/2016/02/asplos277-pct.pdf
    /// </remarks>
    internal class PCTScheduler : IScheduler
    {
        
        
        /// <summary>
        /// Random value generator.
        /// </summary>
        private readonly IRandomValueGenerator _randomValueGenerator;

        /// <summary>
        /// The number of scheduled steps.
        /// </summary>
        private int ScheduledSteps;

        /// <summary>
        /// Max number of priority switch points.
        /// </summary>
        internal readonly int MaxPrioritySwitchPoints;

        /// <summary>
        /// Approximate length of the schedule across all schedules.
        /// </summary>
        internal int ScheduleLength;

        /// <summary>
        /// List of prioritized operations.
        /// </summary>
        private readonly List<ulong> PrioritizedOperations = new();
        
        /// <summary>
        /// Set of priority change points.
        /// </summary>
        private readonly SortedSet<int> PriorityChangePoints = new();
        
        /// <summary>
        /// Initializes a new instance of the <see cref="PCTScheduler"/> class.
        /// </summary>
        public PCTScheduler(int maxPrioritySwitchPoints, int scheduleLength, IRandomValueGenerator random)
        {
            _randomValueGenerator = random;
            ScheduledSteps = 0;
            ScheduleLength = scheduleLength;
            MaxPrioritySwitchPoints = maxPrioritySwitchPoints;
            if (ScheduleLength != 0)
            {
                PreparePriorityChangePoints(ScheduleLength);
            }
            
        }

        public virtual bool GetNextSchedulingChoice(
            SchedulingChoice lastChoice, IEnumerable<SchedulingChoice> choices, out SchedulingChoice next)
        {
            ScheduledSteps++;
            var operations = choices.ToLookup(choice => choice.OperationId);
            var operationId = GetPrioritizedOperation(operations, lastChoice.OperationId);
            var operationChoices = operations[operationId].ToList();
            var choiceIndex = operationChoices.Count == 1 ? 0 : _randomValueGenerator.Next(operationChoices.Count);
            next = operationChoices[choiceIndex];

            return true;
        }

        /// <summary>
        /// Moves the current priority change point forward. This is a useful
        /// optimization when a priority change point is assigned in either a
        /// sequential execution or a nondeterministic choice.
        /// </summary>
        private void MovePriorityChangePointForward()
        {
            PriorityChangePoints.Remove(ScheduledSteps);
            var newPriorityChangePoint = ScheduledSteps + 1;
            while (PriorityChangePoints.Contains(newPriorityChangePoint))
            {
                newPriorityChangePoint++;
            }

            PriorityChangePoints.Add(newPriorityChangePoint);
            Debug.WriteLine("<PCTLog> Moving priority change to '{0}'.", newPriorityChangePoint);
        }
        

        /// <summary>
        /// Returns the prioritized operation.
        /// </summary>
        private ulong GetPrioritizedOperation(ILookup<ulong, SchedulingChoice> operations, ulong currentOperationId)
        {
            if (PrioritizedOperations.Count == 0)
            {
                PrioritizedOperations.Add(currentOperationId);
            }

            foreach (var operation in operations.Where(operation => !PrioritizedOperations.Contains(operation.Key)))
            {
                var mIndex = _randomValueGenerator.Next(PrioritizedOperations.Count) + 1;
                PrioritizedOperations.Insert(mIndex, operation.Key);
                Debug.WriteLine("<PCTLog> Detected new operation '{0}' at index '{1}'.", operation.Key, mIndex);
            }


            if (PriorityChangePoints.Contains(ScheduledSteps))
            {
                if (operations.Count == 1)
                {
                    MovePriorityChangePointForward();
                }
                else
                {
                    var priority = PrioritizedOperations.First(operations.Contains);
                    PrioritizedOperations.Remove(priority);
                    PrioritizedOperations.Add(priority);
                    Debug.WriteLine("<PCTLog> Operation '{0}' changes to lowest priority.", priority);
                }
            }
            var prioritizedSchedulable = PrioritizedOperations.First(operations.Contains);

            if (Debug.IsEnabled)
            {
                Debug.WriteLine("<PCTLog> Prioritized schedulable '{0}'.", prioritizedSchedulable);
                Debug.Write("<PCTLog> Priority list: ");
                for (var idx = 0; idx < PrioritizedOperations.Count; idx++)
                {
                    if (idx < PrioritizedOperations.Count - 1)
                    {
                        Debug.Write("'{0}', ", PrioritizedOperations[idx]);
                    }
                    else
                    {
                        Debug.WriteLine("'{0}'.", PrioritizedOperations[idx]);
                    }
                }
            }

            return prioritizedSchedulable;
        }

        public void Reset()
        {
            ScheduleLength = 0;
            ScheduledSteps = 0;
            PrioritizedOperations.Clear();
        }

        /// <inheritdoc/>
        public virtual bool PrepareForNextIteration()
        {
            ScheduleLength = Math.Max(ScheduleLength, ScheduledSteps);
            ScheduledSteps = 0;

            PrioritizedOperations.Clear();
            PriorityChangePoints.Clear();
            return true;
        }

        private void PreparePriorityChangePoints(int step)
        {
            List<int> listOfInts = new List<int>();
            for (int i = 0; i < step; i++)
            {
                listOfInts.Add(i);
            }

            for (int i = 0; i < MaxPrioritySwitchPoints; i++)
            {
                int index = _randomValueGenerator.Next(listOfInts.Count);
                PriorityChangePoints.Add(listOfInts[index]);
                listOfInts.RemoveAt(index);
                if (listOfInts.Count == 0)
                {
                    break;
                }
            }
        }

        public IScheduler Mutate()
        {
            return new PCTScheduler(MaxPrioritySwitchPoints, ScheduleLength, ((ControlledRandom) _randomValueGenerator).Mutate());
        }

        public IScheduler New()
        {
            return new PCTScheduler(MaxPrioritySwitchPoints, ScheduleLength, ((ControlledRandom) _randomValueGenerator).New());
        }
    }
}
