using PChecker.Runtime.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.SystematicTesting.Operations
{
    /// <summary>
    /// Represents an available choice that can be selected by the scheduler during testing.
    /// </summary>
    internal abstract class SchedulingChoice
    {
        /// <summary>
        /// The operation that will be scheduled. 
        /// </summary>
        public AsyncOperation Operation { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SchedulingChoice"/> class.
        /// </summary>
        protected SchedulingChoice(AsyncOperation operation)
        {
            Operation = operation;
        }
    }

    /// <inheritdoc/>
    internal abstract class SchedulingChoice<TOperation> : SchedulingChoice
        where TOperation : AsyncOperation
    {
        public new TOperation Operation => (TOperation)base.Operation;

        internal SchedulingChoice(TOperation operation)
            : base(operation)
        {
        }
    }

    /// <summary>
    /// Represents delivering an event to a state machine and executing the 
    /// corresponding handler.
    /// </summary>
    internal sealed class DeliverEventChoice : SchedulingChoice<StateMachineOperation>
    {
        /// <summary>
        /// Event that will be delivered to the operation.
        /// </summary>
        public (Event e, EventInfo info) EventToDeliver { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeliverEventChoice"/> class.
        /// </summary>
        internal DeliverEventChoice(StateMachineOperation operation, 
            (Event e, EventInfo info) eventToDeliver)
            : base(operation)
        {
            EventToDeliver = eventToDeliver;
        }
    }

    /// <summary>
    /// Represents resuming execution of an in-progress event handler.
    /// </summary>
    internal sealed class ResumeHandlerChoice : SchedulingChoice<StateMachineOperation>
    {
        /// <summary>
        /// Event whose handler will resume.
        /// </summary>
        public (Event e, EventInfo info) EventToResume { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResumeHandlerChoice"/> class.
        /// </summary>
        internal ResumeHandlerChoice(StateMachineOperation operation, 
            (Event e, EventInfo info) eventToResume)
            : base(operation)
        {
            EventToResume = eventToResume;
        }
    }

    /// <summary>
    /// Represents delivering an event to a state machine currently blocked on a receive. 
    /// </summary>
    internal sealed class CompleteReceiveChoice : SchedulingChoice<StateMachineOperation>
    {
        /// <summary>
        /// Event whose handler called receive and will resume.
        /// </summary>
        public (Event e, EventInfo info) EventToResume { get; }

        /// <summary>
        /// Event that will be delivered to complete the receive.
        /// </summary>
        public (Event e, EventInfo info) EventToDeliver { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompleteReceiveChoice"/> class.
        /// </summary>
        internal CompleteReceiveChoice(
            StateMachineOperation operation,
            (Event e, EventInfo info) eventToResume, 
            (Event e, EventInfo info) eventToDeliver)
            : base(operation)
        {
            EventToResume = eventToResume;
            EventToDeliver = eventToDeliver;
        }
    }

    /// <summary>
    /// Represents executing a TaskOperation. 
    /// </summary>
    internal sealed class RunTaskChoice : SchedulingChoice<TaskOperation>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RunTaskChoice"/> class.
        /// </summary>
        internal RunTaskChoice(TaskOperation operation)
            : base(operation)
        {
        }
    }
}
