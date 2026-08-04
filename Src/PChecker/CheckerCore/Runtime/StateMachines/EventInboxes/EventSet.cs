using PChecker.Runtime.Events;
using PChecker.Runtime.StateMachines.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PChecker.Runtime.StateMachines.EventInboxes
{
    internal sealed class EventSet : EventInbox
    {
        /// <summary>
        /// The internal set that contains events with their metadata.
        /// </summary>
        private readonly HashSet<(Event e, EventInfo info)> Set;

        /// <summary>
        /// Initializes a new instance of the <see cref="EventSet"/> class.
        /// </summary>
        internal EventSet(IStateMachineManager stateMachineManager, StateMachine stateMachine)
            : base(stateMachineManager, stateMachine)
        {
            Set = new HashSet<(Event, EventInfo)>();
        }

        /// <inheritdoc/>
        protected override void AddEventToInbox(Event e, EventInfo info)
        {
            Set.Add((e, info));
        }

        /// <inheritdoc/>
        protected override IEnumerable<(Event e, EventInfo info)> GetEnabledEventsFromInbox(bool checkOnly = false)
        {
            // With event set semantics, ignoring and deferring events are equivalent. 
            // Ignored events should not be removed because the scheduler may want to 
            // delay their delivery until later. 
            HashSet<(Event e, EventInfo info)> enabledEvents = new(
                Set.Where(x => !IsEventIgnored(x.e, x.info) && !IsEventDeferred(x.e, x.info)));
            return enabledEvents;
        }

        /// <inheritdoc/>
        public override void Remove(Event e, EventInfo info)
        {
            Set.Remove((e, info));
        }

        /// <inheritdoc/>
        protected override IEnumerable<(Event e, EventInfo info)> GetReceivedEvents(Dictionary<Type, Func<Event, bool>> eventWaitTypes)
        {
            HashSet<(Event e, EventInfo info)> receivedEvents = new(
                Set.Where(x => IsWaitedEvent(x.e, eventWaitTypes)));
            return receivedEvents;
        }

        /// <inheritdoc/>
        protected override void ClearInbox()
        {
            Set.Clear();
        }

        /// <inheritdoc/>
        protected override IEnumerable<(Event e, EventInfo info)> GetInboxEvents()
        {
            return Set;
        }
    }
}
