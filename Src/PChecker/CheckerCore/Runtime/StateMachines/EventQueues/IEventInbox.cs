// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using PChecker.Runtime.Events;

namespace PChecker.Runtime.StateMachines.EventQueues
{
    /// <summary>
    /// Interface of a queue of events.
    /// </summary>
    internal interface IEventInbox : IDisposable
    {
        /// <summary>
        /// The size of the inbox.
        /// </summary>
        int Size { get; }

        /// <summary>
        /// Checks if an event has been raised.
        /// </summary>
        bool IsEventRaised { get; }

        /// <summary>
        /// Adds the specified event and its optional metadata.
        /// </summary>
        AddEventStatus AddEvent(Event e, EventInfo info);

        /// <summary>
        /// Returns the currently enabled events in the inbox, along with their optional metadata.
        /// </summary>
        (EnabledEventsStatus status, IEnumerable<(Event e, EventInfo info)> events) GetEnabledEvents();

        /// <summary>
        /// Removes the specified event with the specified metadata from the inbox.
        /// </summary>
        void Remove(Event e, EventInfo info);

        /// <summary>
        /// Adds the specified raised event.
        /// </summary>
        void RaiseEvent(Event e);

        /// <summary>
        /// Waits to receive an event of the specified type that satisfies an optional predicate.
        /// </summary>
        Task<Event> ReceiveEventAsync(Type eventType, Func<Event, bool> predicate = null);

        /// <summary>
        /// Waits to receive an event of the specified types.
        /// </summary>
        Task<Event> ReceiveEventAsync(params Type[] eventTypes);

        /// <summary>
        /// Waits to receive an event of the specified types that satisfy the specified predicates.
        /// </summary>
        Task<Event> ReceiveEventAsync(params Tuple<Type, Func<Event, bool>>[] events);

        /// <summary>
        /// Returns the cached state of the inbox.
        /// </summary>
        int GetCachedState();

        /// <summary>
        /// Closes the inbox, which stops any further events from being added.
        /// </summary>
        void Close();
    }
}