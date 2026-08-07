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
        /// The internal list that contains events with their metadata. List is 
        /// used, as opposed to HashSet, to ensure order of enabled events is 
        /// deterministic. 
        /// </summary>
        private readonly List<(Event e, EventInfo info)> Events;

        /// <summary>
        /// Initializes a new instance of the <see cref="EventSet"/> class.
        /// </summary>
        internal EventSet(IStateMachineManager stateMachineManager, StateMachine stateMachine)
            : base(stateMachineManager, stateMachine)
        {
            Events = new();
        }

        /// <inheritdoc/>
        protected override void AddEventToInbox(Event e, EventInfo info)
        {
            Events.Add((e, info));
        }

        /// <inheritdoc/>
        protected override IEnumerable<(Event e, EventInfo info)> GetEnabledEventsFromInbox()
        {
            // With event set semantics, ignoring and deferring events are equivalent. 
            // Ignored events should not be removed because the scheduler may want to 
            // delay their delivery until later. 
            List<(Event e, EventInfo info)> enabledEvents = new(
                Events.Where(x => !IsEventIgnored(x) && !IsEventDeferred(x)));
            return enabledEvents;
        }

        /// <inheritdoc/>
        protected override void RemoveChosenEvent((Event e, EventInfo info) chosenEvent)
        {
            Events.Remove(chosenEvent);
        }

        /// <inheritdoc/>
        protected override IEnumerable<(Event e, EventInfo info)> GetReceivedEvents(Dictionary<Type, Func<Event, bool>> eventWaitTypes)
        {
            List<(Event e, EventInfo info)> receivedEvents = new(
                Events.Where(x => IsWaitedEvent(x.e, eventWaitTypes)));
            return receivedEvents;
        }

        /// <inheritdoc/>
        protected override void RemoveReceivedEvent((Event e, EventInfo info) receivedEvent)
        {
            Events.Remove(receivedEvent);
        }

        /// <inheritdoc/>
        protected override void ClearInbox()
        {
            Events.Clear();
        }

        /// <inheritdoc/>
        protected override IEnumerable<(Event e, EventInfo info)> GetInboxEvents()
        {
            return Events;
        }
    }
}
