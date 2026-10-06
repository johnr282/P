using LanguageExt;
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
    /// Represents an externally-visible effect of state machine execution. Used 
    /// to notify scheduling strategy of the effects of its last scheduling choice.
    /// SchedulingChoice cannot be used for this purpose; an event send is one type
    /// of effect, but it could become either a DeliverEventChoice or a 
    /// CompleteReceiveChoice depending on the context in which it is delivered. 
    /// Additionally, event announcements are not schedulable and thus have no 
    /// corresponding SchedulingChoice.
    /// </summary>
    internal abstract class ExecutionEffect
    {
        /// <summary>
        /// Id of the state machine that produced this effect.
        /// </summary>
        internal StateMachineId StateMachineId { get; }

        internal ExecutionEffect(StateMachineId stateMachineId)
        {
            StateMachineId = stateMachineId;
        }
    }

    /// <summary>
    /// Represents an event send.
    /// </summary>
    internal sealed class SendEffect : ExecutionEffect
    {
        // Snapshot captured at send time, independent of potential mutation
        // by receiver.
        internal Event SentEvent { get; }
        internal StateMachineId TargetStateMachineId { get; }

        /// <summary>
        /// Type of delivery to target available at send time. DeliveryType.Pending
        /// indicates that target cannot currently receive the event.
        /// </summary>
        internal DeliveryType AvailableDeliveryType { get; }

        /// <summary>
        /// Event currently being handled by target at send time. If target is 
        /// initializing, this will be the initial event. If no event is being
        /// handled, this will be (null, null).
        /// </summary>
        internal (Event e, EventInfo info) TargetInProgressEvent { get; }

        internal SendEffect(
            StateMachineId stateMachineId, 
            Event sentEvent, 
            StateMachineId targetStateMachineId,
            DeliveryType availableDeliveryType,
            (Event e, EventInfo info) targetInProgressEvent)
            : base(stateMachineId)
        {
            SentEvent = sentEvent?.Snapshot();
            TargetStateMachineId = targetStateMachineId;
            AvailableDeliveryType = availableDeliveryType;
            TargetInProgressEvent = targetInProgressEvent;
        }

        internal enum DeliveryType
        {
            DeliverEvent,
            CompleteReceive,
            Pending
        }
    }

    /// <summary>
    /// Represents an event observed by a monitor.
    /// </summary>
    internal sealed class MonitorObservationEffect : ExecutionEffect
    { 
        internal Event ObservedEvent { get; }
        internal bool Ignored { get; }

        internal MonitorObservationEffect(
            StateMachineId stateMachineId, 
            Event announcedEvent,
            bool ignored)
            : base(stateMachineId)
        {
            ObservedEvent = announcedEvent?.Snapshot();
            Ignored = ignored;
        }
    }

    /// <summary>
    /// Represents the creation of a new state machine.
    /// </summary>
    internal sealed class CreateEffect : ExecutionEffect
    {
        // Not used for behavior equality; identical machine creations can have
        // different IDs across different runs. 
        internal StateMachineId CreatedStateMachineId { get; }
        internal Event InitialEvent { get; }

        internal CreateEffect(
            StateMachineId stateMachineId, 
            StateMachineId createdStateMachineId,
            Event initialEvent)
            : base(stateMachineId)
        {
            CreatedStateMachineId = createdStateMachineId;
            InitialEvent = initialEvent?.Snapshot();
        }
    }

    /// <summary>
    /// Represents a machine becoming blocked on a receive.
    /// </summary>
    internal sealed class BlockOnReceiveEffect : ExecutionEffect
    { 
        internal (Event e, EventInfo info) InProgressEvent { get; }
        internal IReadOnlyDictionary<Type, Func<Event, bool>> ReceivePredicates { get; }

        internal BlockOnReceiveEffect(
            StateMachineId receivingMachineId,
            (Event e, EventInfo info) inProgressEvent,
            Dictionary<Type, Func<Event, bool>> receivePredicates)
            : base(receivingMachineId)
        {
            InProgressEvent = (inProgressEvent.e?.Snapshot(), inProgressEvent.info);
            ReceivePredicates = new Dictionary<Type, Func<Event, bool>>(receivePredicates);
        }
    }

    /// <summary>
    /// Represents a machine completing an event handler. This includes completing
    /// initialization.
    /// </summary>
    internal sealed class CompleteHandlerEffect : ExecutionEffect
    { 
        internal (Event e, EventInfo info) EventOfCompletedHandler { get; }

        internal CompleteHandlerEffect(
            StateMachineId completedMachineId,
            (Event e, EventInfo info) eventOfCompletedHandler)
            : base(completedMachineId)
        {
            EventOfCompletedHandler = (eventOfCompletedHandler.e?.Snapshot(), 
                eventOfCompletedHandler.info);
        }
    }

}
