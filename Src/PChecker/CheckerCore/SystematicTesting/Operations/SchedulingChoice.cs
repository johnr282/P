using PChecker.Runtime.Events;
using PChecker.Runtime.StateMachines;
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
        /// The Id of the operation that will be scheduled.
        /// </summary>
        public ulong OperationId { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SchedulingChoice"/> class.
        /// </summary>
        protected SchedulingChoice(ulong operationId)
        {
            OperationId = operationId;
        }

        /// <summary>
        /// Captures inputs before execution can mutate them. The result is for
        /// observation only: delivery must continue to use the original choice.
        /// Operation identity and event provenance are shared; payloads are copied.
        /// </summary>
        internal SchedulingChoice Snapshot() => this switch
        {
            InitializeChoice c => 
                new InitializeChoice(c.OperationId, c.StateMachineId, 
                    c.InitialEvent?.Snapshot()),
            ResumeInitializationChoice c =>
                new ResumeInitializationChoice(c.OperationId, c.StateMachineId, 
                    c.InitialEvent?.Snapshot()),
            DeliverEventChoice c => 
                new DeliverEventChoice(c.OperationId, c.StateMachineId, 
                    SnapshotEventWithMetadata(c.EventToDeliver)),
            ResumeHandlerChoice c => 
                new ResumeHandlerChoice(c.OperationId, c.StateMachineId, 
                    SnapshotEventWithMetadata(c.EventToResume)),
            CompleteReceiveChoice c => 
                new CompleteReceiveChoice(c.OperationId, c.StateMachineId,
                    SnapshotEventWithMetadata(c.EventToResume), 
                    SnapshotEventWithMetadata(c.EventToDeliver)),
            RunTaskChoice c => new RunTaskChoice(c.OperationId),

            _ => throw new NotSupportedException(
                $"Unsupported scheduling choice: {GetType()}")
        };

        private static (Event e, EventInfo info) SnapshotEventWithMetadata(
            (Event e, EventInfo info) input) =>
            (input.e?.Snapshot(), input.info);

        /// <summary>
        /// Returns the creation path of the state machine corresponding to 
        /// choice, or null if choice has no corresponding machine. 
        /// </summary>
        public MachineCreationPath GetCreationPath() => 
            GetStateMachineId()?.CreationPath;

        /// <summary>
        /// Returns the id of the state machine corresponding to choice, or null
        /// if choice has no corresponding machine.
        /// </summary>
        public StateMachineId GetStateMachineId() => this switch
        {
            StateMachineSchedulingChoice machineChoice => machineChoice.StateMachineId,
            _ => null
        };
    }

    /// <summary>
    /// Scheduling choice with an associated state machine.
    /// </summary>
    internal abstract class StateMachineSchedulingChoice : SchedulingChoice
    {
        /// <summary>
        /// Id for the state machine that will be scheduled if this choice is
        /// chosen.
        /// </summary>
        public StateMachineId StateMachineId { get; }

        internal StateMachineSchedulingChoice(
            ulong operationId,
            StateMachineId machineId)
            : base(operationId)
        {
            StateMachineId = machineId;
        }

        /// <summary>
        /// Returns the event potentially sent to monitors when this choice is 
        /// executed, or null if no event is sent.
        /// </summary>
        /// <returns></returns>
        public Event GetMonitoredEvent()
        {
            return this switch
            {
                InitializeChoice => null,
                ResumeInitializationChoice => null,
                DeliverEventChoice deliver => deliver.EventToDeliver.e,
                ResumeHandlerChoice => null,
                CompleteReceiveChoice receive => receive.EventToDeliver.e,

                _ => throw new NotSupportedException(
                    $"Unsupported scheduling choice: {this.GetType()}")
            };

        }
    }

    /// <summary>
    /// Represents the initialization of a newly created state machine, which is 
    /// the execution of its start state's entry function. Initialization choices
    /// must be distinct from normal event handling choices (<see cref="DeliverEventChoice"/>, 
    /// <see cref="ResumeHandlerChoice"/>) because initializing a state machine with 
    /// event e and handling event e after initialization are distinct behaviors. 
    /// </summary>
    internal sealed class InitializeChoice : StateMachineSchedulingChoice
    {
        /// <summary>
        /// Event passed to the initial entry function; no EventInfo because initial
        /// event has no meaningful origin. 
        /// </summary>
        public Event InitialEvent { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="InitializeChoice"/> class.
        /// </summary>
        internal InitializeChoice(
            ulong operationId, 
            StateMachineId machineToInitializeId,
            Event initialEvent)
            : base(operationId, machineToInitializeId)
        {
            InitialEvent = initialEvent;
        }
    }

    /// <summary>
    /// Represents resuming execution of the entry function of a state machine's
    /// initial state.
    /// </summary>
    internal sealed class ResumeInitializationChoice : StateMachineSchedulingChoice
    {
        /// <summary>
        /// Event passed to the initial entry function. 
        /// </summary>
        public Event InitialEvent { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="InitializeChoice"/> class.
        /// </summary>
        internal ResumeInitializationChoice(
            ulong operationId, 
            StateMachineId machineToResumeId,
            Event initialEvent)
            : base(operationId, machineToResumeId)
        {
            InitialEvent = initialEvent;
        }
    }

    /// <summary>
    /// Represents delivering an event to a state machine and executing the 
    /// corresponding handler.
    /// </summary>
    internal sealed class DeliverEventChoice : StateMachineSchedulingChoice
    {
        /// <summary>
        /// Event that will be delivered to the operation.
        /// </summary>
        public (Event e, EventInfo info) EventToDeliver { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeliverEventChoice"/> class.
        /// </summary>
        internal DeliverEventChoice(
            ulong operationId, 
            StateMachineId targetMachineId,
            (Event e, EventInfo info) eventToDeliver)
            : base(operationId, targetMachineId)
        {
            EventToDeliver = eventToDeliver;
        }
    }

    /// <summary>
    /// Represents resuming execution of an in-progress event handler.
    /// </summary>
    internal sealed class ResumeHandlerChoice : StateMachineSchedulingChoice
    {
        /// <summary>
        /// Event whose handler will resume.
        /// </summary>
        public (Event e, EventInfo info) EventToResume { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResumeHandlerChoice"/> class.
        /// </summary>
        internal ResumeHandlerChoice(
            ulong operationId, 
            StateMachineId machineToResumeId,
            (Event e, EventInfo info) eventToResume)
            : base(operationId, machineToResumeId)
        {
            EventToResume = eventToResume;
        }
    }

    /// <summary>
    /// Represents delivering an event to a state machine currently blocked on a receive. 
    /// </summary>
    internal sealed class CompleteReceiveChoice : StateMachineSchedulingChoice
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
            ulong operationId, 
            StateMachineId machineToReceiveId,
            (Event e, EventInfo info) eventToResume, 
            (Event e, EventInfo info) eventToDeliver)
            : base(operationId, machineToReceiveId)
        {
            EventToResume = eventToResume;
            EventToDeliver = eventToDeliver;
        }
    }

    /// <summary>
    /// Represents executing a TaskOperation. 
    /// </summary>
    internal sealed class RunTaskChoice : SchedulingChoice
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RunTaskChoice"/> class.
        /// </summary>
        internal RunTaskChoice(ulong operationId)
            : base(operationId)
        {
        }
    }
}
