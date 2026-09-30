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
    /// Represents an event announce.
    /// </summary>
    internal sealed class AnnounceEffect : ExecutionEffect
    { 
        // Snapshot captured before monitors execute on the live announcement.
        internal Event AnnouncedEvent { get; }

        internal AnnounceEffect(
            StateMachineId stateMachineId, 
            Event announcedEvent)
            : base(stateMachineId)
        {
            AnnouncedEvent = announcedEvent?.Snapshot();
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
        internal Type CreatedStateMachineType { get; }
        internal string CreatedStateMachineName { get; }
        internal Event InitialEvent { get; }

        internal CreateEffect(StateMachineId stateMachineId, 
            StateMachineId createdStateMachineId,
            Type createdStateMachineType,
            string createdStateMachineName,
            Event initialEvent)
            : base(stateMachineId)
        {
            CreatedStateMachineId = createdStateMachineId;
            CreatedStateMachineType = createdStateMachineType;
            CreatedStateMachineName = createdStateMachineName;
            InitialEvent = initialEvent?.Snapshot();
        }
    }
}
