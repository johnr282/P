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
    /// Represents the initialization of a newly created state machine, which is 
    /// the execution of its start state's entry function. Initialization choices
    /// must be distinct from normal event handling choices (<see cref="DeliverEventChoice"/>, 
    /// <see cref="ResumeHandlerChoice"/>) because initializing a state machine with 
    /// event e and handling event e after initialization are distinct behaviors. 
    /// </summary>
    internal sealed class InitializeChoice : SchedulingChoice<StateMachineOperation>
    {
        /// <summary>
        /// Event passed to the initial entry function; no EventInfo because initial
        /// event has no meaningful origin. 
        /// </summary>
        public Event InitialEvent { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="InitializeChoice"/> class.
        /// </summary>
        internal InitializeChoice(StateMachineOperation operation,
            Event initialEvent)
            : base(operation)
        {
            InitialEvent = initialEvent;
        }
    }

    /// <summary>
    /// Represents resuming execution of the entry function of a state machine's
    /// initial state.
    /// </summary>
    internal sealed class ResumeInitializationChoice : SchedulingChoice<StateMachineOperation>
    {
        /// <summary>
        /// Event passed to the initial entry function. 
        /// </summary>
        public Event InitialEvent { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="InitializeChoice"/> class.
        /// </summary>
        internal ResumeInitializationChoice(StateMachineOperation operation,
            Event initialEvent)
            : base(operation)
        {
            InitialEvent = initialEvent;
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
        /// True if receive occurred during initialization. 
        /// </summary>
        public bool InInitialization { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompleteReceiveChoice"/> class.
        /// </summary>
        internal CompleteReceiveChoice(
            StateMachineOperation operation,
            (Event e, EventInfo info) eventToResume, 
            (Event e, EventInfo info) eventToDeliver,
            bool inInitialization)
            : base(operation)
        {
            EventToResume = eventToResume;
            EventToDeliver = eventToDeliver;
            InInitialization = inInitialization;
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
