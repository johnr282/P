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
        internal Event SentEvent { get; }
        internal StateMachineId TargetStateMachineId { get; }

        internal SendEffect(StateMachineId stateMachineId, 
            Event sentEvent, 
            StateMachineId targetStateMachineId)
            : base(stateMachineId)
        {
            SentEvent = sentEvent;
            TargetStateMachineId = targetStateMachineId;
        }
    }

    /// <summary>
    /// Represents an event announce.
    /// </summary>
    internal sealed class AnnounceEffect : ExecutionEffect
    { 
        internal Event AnnouncedEvent { get; }

        internal AnnounceEffect(StateMachineId stateMachineId, 
            Event announcedEvent)
            : base(stateMachineId)
        {
            AnnouncedEvent = announcedEvent;
        }
    }

    /// <summary>
    /// Represents the creation of a new state machine.
    /// </summary>
    internal sealed class CreateEffect : ExecutionEffect
    {
        internal StateMachineId CreatedStateMachineId { get; }

        internal CreateEffect(StateMachineId stateMachineId, 
            StateMachineId createdStateMachineId)
            : base(stateMachineId)
        {
            CreatedStateMachineId = createdStateMachineId;
        }
    }
}
