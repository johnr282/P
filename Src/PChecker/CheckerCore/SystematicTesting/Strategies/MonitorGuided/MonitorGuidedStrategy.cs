using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PChecker.Runtime.Specifications;
using PChecker.SystematicTesting.Operations;
using Plang.Compiler.TypeChecker.AST.Declarations;

namespace PChecker.SystematicTesting.Strategies.MonitorGuided
{
    internal class MonitorGuidedStrategy : ISchedulingStrategy
    {
        private Dictionary<Machine, Monitor> monitors = new();

        public MonitorGuidedStrategy()
        {
            Console.WriteLine("MonitorGuidedStrategy initialized.");
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
            throw new NotImplementedException();
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
