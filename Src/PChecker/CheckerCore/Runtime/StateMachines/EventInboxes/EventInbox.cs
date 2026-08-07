using PChecker.Runtime.Events;
using PChecker.Runtime.Exceptions;
using PChecker.Runtime.StateMachines.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PChecker.Runtime.StateMachines.EventInboxes
{
    /// <summary>
    /// An abstract event inbox for a state machine.
    /// </summary>
    internal abstract class EventInbox : IEventInbox
    {
        /// <summary>
        /// Manages the state machine that owns this queue.
        /// </summary>
        private readonly IStateMachineManager StateMachineManager;

        /// <summary>
        /// The state machine that owns this queue.
        /// </summary>
        private readonly StateMachine StateMachine;

        /// <summary>
        /// Map from the types of events that the owner of the queue is waiting to receive
        /// to an optional predicate. If an event of one of these types is enqueued, then
        /// if there is no predicate, or if there is a predicate and evaluates to true, then
        /// the event is received, else the event is deferred.
        /// </summary>
        private Dictionary<Type, Func<Event, bool>> EventWaitTypes;

        /// <summary>
        /// Task completion source that contains the event obtained using an explicit receive.
        /// </summary>
        private TaskCompletionSource<Event> ReceiveCompletionSource;

        /// <summary>
        /// Checks if the queue is accepting new events.
        /// </summary>
        private bool IsClosed;

        /// <inheritdoc/>
        public bool IsReceivePending { get; private set; }

        /// <inheritdoc/>
        public int Size => GetInboxEvents().Count();

        /// <inheritdoc/>
        public (Event e, EventInfo info) RaisedEvent { get; private set; } = default;

        /// <inheritdoc/>
        public bool IsEventRaised => RaisedEvent != default;

        /// <inheritdoc/>
        public InboxStatus Status => GetEnabledEvents().status;

        internal EventInbox(IStateMachineManager stateMachineManager, StateMachine stateMachine)
        {
            StateMachineManager = stateMachineManager;
            StateMachine = stateMachine;
            EventWaitTypes = new Dictionary<Type, Func<Event, bool>>();
            IsClosed = false;
        }

        /// <inheritdoc/>
        public AddEventStatus AddEvent(Event e, EventInfo info)
        {
            if (IsClosed)
            {
                return AddEventStatus.Dropped;
            }

            StateMachineManager.OnEnqueueEvent(e, info);
            AddEventToInbox(e, info);

            if (!StateMachineManager.IsEventHandlerRunning)
            {
                if (!GetEnabledEventsFromInbox().Any())
                {
                    return AddEventStatus.NoEventsAvailable;
                }

                StateMachineManager.IsEventHandlerRunning = true;
                return AddEventStatus.EventHandlerNotRunning;
            }

            return AddEventStatus.EventHandlerRunning;
        }

        /// <summary>
        /// Adds the specified event and its optional metadata to the inbox.
        /// </summary>
        protected abstract void AddEventToInbox(Event e, EventInfo info);

        /// <inheritdoc/>
        public (InboxStatus status, IEnumerable<(Event e, EventInfo info)> events) GetEnabledEvents()
        {
            List<(Event e, EventInfo info)> events = new();

            // Try to get the raised event, if there is one. Raised events
            // have priority over the events in the inbox.
            // TODO: should the user be able to raise an ignored event?
            if (IsEventRaised && !IsEventIgnored(RaisedEvent))
            {
                events.Add(RaisedEvent);
                return (InboxStatus.Raised, events);
            }

            if (IsReceivePending)
            {
                // Don't need a separate status for pending receive because state
                // machine will be currently executing a handler, not starting a
                // new event loop iteration, which is where status is checked.
                return (InboxStatus.EventsEnabled, GetReceivedEvents(EventWaitTypes));
            }

            IEnumerable<(Event e, EventInfo info)> enabledEvents = GetEnabledEventsFromInbox();
            if (enabledEvents.Any())
            {
                return (InboxStatus.EventsEnabled, enabledEvents);
            }

            // No event are enabled, so check if there is a default event handler.
            var hasDefaultHandler = StateMachineManager.IsDefaultHandlerAvailable();
            if (!hasDefaultHandler)
            {
                // There is no default event handler installed, so do not return an event.
                StateMachineManager.IsEventHandlerRunning = false;
                return (InboxStatus.NoEventsEnabled, events);
            }

            // TODO: check op-id of default event.
            // A default event handler exists.
            events.Add(DefaultEvent.InstanceWithInfo(StateMachine));
            return (InboxStatus.Default, events);
        }

        /// <summary>
        /// Returns the currently enabled events in the inbox, along with their optional 
        /// metadata. Must not modify inbox state. Returned events must be ordered 
        /// deterministically.
        /// </summary>
        protected abstract IEnumerable<(Event e, EventInfo info)> GetEnabledEventsFromInbox();

        /// <summary>
        /// Returns whether the specified event is ignored in the state machine's current state.
        /// </summary>
        protected bool IsEventIgnored((Event e, EventInfo info) e)
        {
            return StateMachineManager.IsEventIgnored(e.e, e.info);
        }

        /// <summary>
        /// Returns whether the specified event is deferred in the state machine's current state.
        /// </summary>
        protected bool IsEventDeferred((Event e, EventInfo info) e)
        {
            return StateMachineManager.IsEventDeferred(e.e, e.info);
        }

        /// <inheritdoc/>
        public void NotifyChosenEvent((Event e, EventInfo info) chosenEvent)
        {
            if (IsEventRaised)
            {
                // If a raised event was not chosen, it must be ignored in the current state.
                if (chosenEvent != RaisedEvent && !IsEventIgnored(RaisedEvent))
                {
                    throw new PInternalException(
                        "Raised event was not chosen, but is not ignored.");
                }

                // Regardless of whether raised event was chosen or is ignored in the 
                // current state, clear it. 
                RaisedEvent = default;
                return;
            }

            if (DefaultEvent.IsDefaultEvent(chosenEvent.e))
            {
                return;
            }
            
            RemoveChosenEvent(chosenEvent);
        }

        /// <summary>
        /// Removes the specified chosen event from the inbox.
        /// </summary>
        protected abstract void RemoveChosenEvent((Event e, EventInfo info) chosenEvent);

        /// <inheritdoc/>
        public void RaiseEvent(Event e)
        {
            var stateName = StateMachine.CurrentState.GetType().Name;
            var eventOrigin = new EventOriginInfo(StateMachine.Id, StateMachine.GetType().FullName, stateName);
            var info = new EventInfo(e, eventOrigin, StateMachine.VectorTime);
            RaisedEvent = (e, info);
            StateMachineManager.OnRaiseEvent(e, info);
        }

        /// <inheritdoc/>
        public Task<Event> ReceiveEventAsync(Type eventType, Func<Event, bool> predicate = null)
        {
            var eventWaitTypes = new Dictionary<Type, Func<Event, bool>>
            {
                { eventType, predicate }
            };

            return ReceiveEventAsync(eventWaitTypes);
        }

        /// <inheritdoc/>
        public Task<Event> ReceiveEventAsync(params Type[] eventTypes)
        {
            var eventWaitTypes = new Dictionary<Type, Func<Event, bool>>();
            foreach (var type in eventTypes)
            {
                eventWaitTypes.Add(type, null);
            }

            return ReceiveEventAsync(eventWaitTypes);
        }

        /// <inheritdoc/>
        public Task<Event> ReceiveEventAsync(params Tuple<Type, Func<Event, bool>>[] events)
        {
            var eventWaitTypes = new Dictionary<Type, Func<Event, bool>>();
            foreach (var e in events)
            {
                eventWaitTypes.Add(e.Item1, e.Item2);
            }

            return ReceiveEventAsync(eventWaitTypes);
        }

        /// <summary>
        /// Waits for an event to be enqueued.
        /// </summary>
        private Task<Event> ReceiveEventAsync(Dictionary<Type, Func<Event, bool>> eventWaitTypes)
        {
            StateMachine.Runtime.NotifyReceiveCalled(StateMachine);
            IsReceivePending = true;
            ReceiveCompletionSource = new TaskCompletionSource<Event>();
            EventWaitTypes = eventWaitTypes;
            StateMachineManager.OnWaitEvent(EventWaitTypes.Keys);
            return ReceiveCompletionSource.Task;
        }

        /// <summary>
        /// Returns events in inbox that match the specified event types and predicates
        /// and could be delivered to the inbox's state machine.
        /// </summary>
        protected abstract IEnumerable<(Event e, EventInfo info)> GetReceivedEvents(Dictionary<Type, Func<Event, bool>> eventWaitTypes);

        /// <summary>
        /// Returns whether the specified event matches the specified event types and predicates.
        /// </summary>
        protected static bool IsWaitedEvent(Event e, Dictionary<Type, Func<Event, bool>> eventWaitTypes)
        {
            return eventWaitTypes.TryGetValue(e.GetType(), out var predicate) &&
                (predicate is null || predicate(e));
        }

        /// <inheritdoc/>
        public void CompleteReceive(Event e, EventInfo info)
        {
            IsReceivePending = false;
            EventWaitTypes.Clear();
            StateMachineManager.OnReceiveEvent(e, info);
            ReceiveCompletionSource.SetResult(e);
        }

        /// <inheritdoc/>
        public bool IsBlockedOnReceive()
        {
            return IsReceivePending && !GetReceivedEvents(EventWaitTypes).Any();
        }

        /// <inheritdoc/>
        public int GetCachedState()
        {
            IEnumerable<(Event e, EventInfo info)> inboxEvents = GetInboxEvents();
            unchecked
            {
                var hash = 19;
                foreach (var (_, info) in inboxEvents)
                {
                    hash = (hash * 31) + info.EventName.GetHashCode();
                }

                return hash;
            }
        }

        /// <inheritdoc/>
        public void Close()
        {
            IsClosed = true;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            IEnumerable<(Event e, EventInfo info)> inboxEvents = GetInboxEvents();
            foreach (var (e, info) in inboxEvents)
            {
                StateMachineManager.OnDropEvent(e, info);
            }

            ClearInbox();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Calls Clear() (or equivalent) on all collections used by inbox to store events.
        /// </summary>
        protected abstract void ClearInbox();

        /// <summary>
        /// Returns all events in the inbox with their metadata. As long as it is 
        /// deterministic, event order does not matter.
        /// </summary>
        protected abstract IEnumerable<(Event e, EventInfo info)> GetInboxEvents();
    }
}
