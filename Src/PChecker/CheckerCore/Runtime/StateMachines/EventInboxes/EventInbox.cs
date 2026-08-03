using PChecker.Runtime.Events;
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
        /// The raised event and its metadata, or null if no event has been raised.
        /// </summary>
        private (Event e, EventInfo info) RaisedEvent;

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
        public int Size => GetInboxEvents().Count();

        /// <inheritdoc/>
        public bool IsEventRaised => RaisedEvent != default;

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

            if (IsWaitedEvent(e, EventWaitTypes))
            {
                EventWaitTypes.Clear();
                StateMachineManager.OnReceiveEvent(e, info);
                ReceiveCompletionSource.SetResult(e);
                return AddEventStatus.EventHandlerRunning;
            }

            StateMachineManager.OnEnqueueEvent(e, info);
            AddEventToInbox(e, info);

            if (!StateMachineManager.IsEventHandlerRunning)
            {
                if (!GetEnabledEventsFromInbox(true).Any())
                {
                    return AddEventStatus.NoEventsAvailable;
                }

                StateMachineManager.IsEventHandlerRunning = true;
                return AddEventStatus.EventHandlerNotRunning;
            }

            return AddEventStatus.EventHandlerRunning;
        }

        protected abstract void AddEventToInbox(Event e, EventInfo info);

        /// <inheritdoc/>
        public (EnabledEventsStatus status, IEnumerable<(Event e, EventInfo info)> events) GetEnabledEvents()
        {
            HashSet<(Event e, EventInfo info)> events = new();

            // Try to get the raised event, if there is one. Raised events
            // have priority over the events in the inbox.
            if (RaisedEvent != default)
            {
                if (StateMachineManager.IsEventIgnored(RaisedEvent.e, RaisedEvent.info))
                {
                    // TODO: should the user be able to raise an ignored event?
                    // The raised event is ignored in the current state.
                    RaisedEvent = default;
                }
                else
                {
                    var raisedEvent = RaisedEvent;
                    RaisedEvent = default;
                    events.Add(raisedEvent);
                    return (EnabledEventsStatus.Raised, events);
                }
            }

            var hasDefaultHandler = StateMachineManager.IsDefaultHandlerAvailable();
            if (hasDefaultHandler)
            {
                StateMachine.Runtime.NotifyDefaultEventHandlerCheck(StateMachine);
            }

            IEnumerable<(Event e, EventInfo info)> enabledEvents = GetEnabledEventsFromInbox();
            if (enabledEvents.Any())
            {
                return (EnabledEventsStatus.Success, enabledEvents);
            }

            // No event are enabled, so check if there is a default event handler.
            if (!hasDefaultHandler)
            {
                // There is no default event handler installed, so do not return an event.
                StateMachineManager.IsEventHandlerRunning = false;
                return (EnabledEventsStatus.NotAvailable, events);
            }

            // TODO: check op-id of default event.
            // A default event handler exists.
            var stateName = StateMachine.CurrentState.GetType().Name;
            var eventOrigin = new EventOriginInfo(StateMachine.Id, StateMachine.GetType().FullName, stateName);
            events.Add((DefaultEvent.Instance,
                new EventInfo(DefaultEvent.Instance, eventOrigin, StateMachine.VectorTime)));
            return (EnabledEventsStatus.Default, events);
        }

        /// <summary>
        /// Returns the currently enabled events in the inbox, along with their optional 
        /// metadata. Must not remove the returned events from the inbox. If checkOnly is
        /// false, may remove ignored events if necessary. 
        /// </summary>
        protected abstract IEnumerable<(Event e, EventInfo info)> GetEnabledEventsFromInbox(bool checkOnly = false);

        /// <summary>
        /// Returns whether the specified event is ignored in the state machine's current state.
        /// </summary>
        protected bool IsEventIgnored(Event e, EventInfo info)
        {
            return StateMachineManager.IsEventIgnored(e, info);
        }

        /// <summary>
        /// Returns whether the specified event is deferred in the state machine's current state.
        /// </summary>
        protected bool IsEventDeferred(Event e, EventInfo info)
        {
            return StateMachineManager.IsEventDeferred(e, info);
        }

        /// <inheritdoc/>
        public abstract void Remove(Event e, EventInfo info);

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

            (Event e, EventInfo info) receivedEvent = FindReceivedEvent(eventWaitTypes);

            if (receivedEvent == default)
            {
                ReceiveCompletionSource = new TaskCompletionSource<Event>();
                EventWaitTypes = eventWaitTypes;
                StateMachineManager.OnWaitEvent(EventWaitTypes.Keys);
                return ReceiveCompletionSource.Task;
            }

            StateMachineManager.OnReceiveEventWithoutWaiting(receivedEvent.e, receivedEvent.info);
            return Task.FromResult(receivedEvent.e);
        }

        /// <summary>
        /// Returns the first event that matches the specified event types and predicates, 
        /// or default if no such event exists.
        /// </summary>
        protected abstract (Event e, EventInfo info) FindReceivedEvent(Dictionary<Type, Func<Event, bool>> eventWaitTypes);

        /// <summary>
        /// Returns whether the specified event matches the specified event types and predicates.
        /// </summary>
        protected static bool IsWaitedEvent(Event e, Dictionary<Type, Func<Event, bool>> eventWaitTypes)
        {
            return eventWaitTypes.TryGetValue(e.GetType(), out var predicate) &&
                (predicate is null || predicate(e));
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
        /// Returns all events in the inbox with their metadata. Only used when event 
        /// ordering doesn't matter.
        /// </summary>
        protected abstract IEnumerable<(Event e, EventInfo info)> GetInboxEvents();
    }
}
