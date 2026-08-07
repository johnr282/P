// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using PChecker.Runtime.Events;

namespace PChecker.Runtime.StateMachines.EventInboxes
{
    /// <summary>
    /// Interface of an event inbox.
    /// </summary>
    internal interface IEventInbox : IDisposable
    {
        /// <summary>
        /// The size of the inbox.
        /// </summary>
        int Size { get; }

        /// <summary>
        /// The currently raised event.
        /// </summary>
        (Event e, EventInfo info) RaisedEvent { get; }

        /// <summary>
        /// Checks if an event has been raised.
        /// </summary>
        bool IsEventRaised { get; }

        /// <summary>
        /// Checks if this inbox has a pending receive.
        /// </summary>
        bool IsReceivePending { get; }

        /// <summary>
        /// The current inbox status.
        /// </summary>
        InboxStatus Status { get; }

        /// <summary>
        /// Adds the specified event and its optional metadata.
        /// </summary>
        AddEventStatus AddEvent(Event e, EventInfo info);

        /// <summary>
        /// Returns the currently enabled events in the inbox, along with their optional metadata.
        /// Must not modify inbox state. To allow for deterministic replay, returned events must 
        /// be ordered deterministically.
        /// </summary>
        (InboxStatus status, IEnumerable<(Event e, EventInfo info)> events) GetEnabledEvents();

        /// <summary>
        /// Notifies inbox that the specified enabled event has been chosen for execution.
        /// </summary>
        void NotifyChosenEvent((Event e, EventInfo info) chosenEvent);

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
        /// Notifies inbox that its state machine's pending receive has completed with 
        /// the specified event. 
        /// </summary>
        void CompleteReceive(Event e, EventInfo info);

        /// <summary>
        /// Returns whether inbox's state machine is currently blocked on a receive, 
        /// meaning there are no events in the inbox that could complete the pending receive. 
        /// </summary>
        bool IsBlockedOnReceive();

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