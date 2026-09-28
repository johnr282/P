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
        // For now, assume that there is only one monitor.
        private Monitor _monitor;
        private MonitorAnalyzer _monitorAnalyzer;

        private readonly uint maxObservedEventsForMonitorExploration;

        public MonitorGuidedStrategy()
        {
            // JR TODO: Add this to CheckerConfiguration
            maxObservedEventsForMonitorExploration = 100;
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

            bool monitorViolationFound = _monitorAnalyzer.GetMonitorGuidance(
                _monitor.CurrentStateName,
                out var guidance);

            if (!monitorViolationFound)
            {
                return GetFallbackChoice(lastChoice, choices, out next);
            }

            throw new NotImplementedException();
        }

        private bool GetFallbackChoice(
            SchedulingChoice lastChoice,
            IEnumerable<SchedulingChoice> choices,
            out SchedulingChoice next)
        {
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
            _monitor = null;
            _monitorAnalyzer = null;
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
