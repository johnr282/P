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
        // Snapshot captured at send time, independent of subsequent receiver execution.
        internal Event SentEvent { get; }
        internal StateMachineId TargetStateMachineId { get; }

        internal SendEffect(
            StateMachineId stateMachineId, 
            Event sentEvent, 
            StateMachineId targetStateMachineId)
            : base(stateMachineId)
        {
            SentEvent = sentEvent?.Snapshot();
            TargetStateMachineId = targetStateMachineId;
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
}
