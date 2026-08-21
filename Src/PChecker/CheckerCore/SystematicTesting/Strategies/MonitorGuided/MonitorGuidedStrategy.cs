using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PChecker.IO.Debugging;
using PChecker.Runtime.Specifications;
using PChecker.SystematicTesting.Operations;
using Plang.Compiler.TypeChecker.AST.Declarations;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    internal class MonitorGuidedStrategy : ISchedulingStrategy
    {
        private readonly Dictionary<Machine, Monitor> monitors = new();

        private readonly uint maxObservedEventsForMonitorExploration;

        public MonitorGuidedStrategy()
        {
            // JR TODO: Add this to CheckerConfiguration
            maxObservedEventsForMonitorExploration = 100;
        }

        /// <summary>
        /// Registers the given monitor AST with its corresponding runtime 
        /// monitor instance.
        /// </summary>
        public void RegisterMonitor(Machine monitorAST, Monitor monitor)
        {
            monitors[monitorAST] = monitor;
        }

        /// <inheritdoc/>
        public virtual bool GetNextSchedulingChoice(
            SchedulingChoice lastChoice,
            IEnumerable<SchedulingChoice> choices,
            out SchedulingChoice next)
        {
            next = null;

            foreach (var (monitorAST, monitor) in monitors)
            {
                if (monitor is not IMonitorFieldProvider fieldProvider)
                {
                    Error.CheckerReportAndExit(
                        $"Monitor {monitorAST.Name} does not implement IMonitorFieldProvider " +
                        $"required by MonitorGuided strategy.");
                    return false;
                }

                var monitorFieldValues = fieldProvider.GetFieldValues();
                var monitorState = monitor.CurrentStateName;

                // JR TODO: Write monitor exploration procedure
            }

            throw new NotImplementedException();
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
            monitors.Clear();
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
    }
}
