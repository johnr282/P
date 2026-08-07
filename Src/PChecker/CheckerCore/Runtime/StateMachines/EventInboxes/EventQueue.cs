// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using PChecker.Runtime.Events;
using PChecker.Runtime.StateMachines.Managers;
using PChecker.Runtime.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PChecker.Runtime.StateMachines.EventInboxes
{
    /// <summary>
    /// Implements a queue of events that is used during testing.
    /// </summary>
    internal sealed class EventQueue : EventInbox
    {
        /// <summary>
        /// The internal queue that contains events with their metadata.
        /// </summary>
        private readonly LinkedList<(Event e, EventInfo info)> Queue;

        /// <summary>
        /// Initializes a new instance of the <see cref="EventQueue"/> class.
        /// </summary>
        internal EventQueue(IStateMachineManager stateMachineManager, StateMachine stateMachine)
            : base(stateMachineManager, stateMachine)
        {
            Queue = new LinkedList<(Event, EventInfo)>();
        }

        /// <inheritdoc/>
        protected override void AddEventToInbox(Event e, EventInfo info)
        {
            Queue.AddLast((e, info));
        }

        /// <inheritdoc/>
        protected override IEnumerable<(Event e, EventInfo info)> GetEnabledEventsFromInbox()
        {
            HashSet<(Event e, EventInfo info)> events = new();
            (Event, EventInfo) nextEvent = TryDequeueEvent(true);
            if (nextEvent != default)
            {
                events.Add(nextEvent);
            }

            return events;
        }

        /// <summary>
        /// Dequeues the next event and its metadata, if there is one available, else returns null.
        /// </summary>
        private (Event e, EventInfo info) TryDequeueEvent(bool checkOnly)
        {
            (Event, EventInfo) nextAvailableEvent = default;

            // Iterates through the events and metadata in the inbox.
            var node = Queue.First;
            while (node != null)
            {
                var nextNode = node.Next;
                var currentEvent = node.Value;

                if (IsEventIgnored(currentEvent))
                {
                    if (!checkOnly)
                    {
                        // Removes an ignored event.
                        Queue.Remove(node);
                    }

                    node = nextNode;
                    continue;
                }

                // Skips a deferred event.
                if (!IsEventDeferred(currentEvent))
                {
                    if (!checkOnly)
                    {
                        Queue.Remove(node);
                    }
                    nextAvailableEvent = currentEvent;
                    break;
                }

                node = nextNode;
            }

            return nextAvailableEvent;
        }

        /// <inheritdoc/>
        protected override void RemoveChosenEvent((Event e, EventInfo info) chosenEvent)
        {
            // Removes chosen event and all prior ignored events from the queue.
            var dequeuedEvent = TryDequeueEvent(false);
            if (dequeuedEvent != chosenEvent)
            {
                throw new PInternalException(
                    "Chosen event does not match next dequeued event.");
            }
        }

        /// <inheritdoc/>
        protected override IEnumerable<(Event e, EventInfo info)> GetReceivedEvents(Dictionary<Type, Func<Event, bool>> eventWaitTypes)
        {
            (Event e, EventInfo info) receivedEvent = Queue.FirstOrDefault(
                x => IsWaitedEvent(x.e, eventWaitTypes));

            HashSet<(Event e, EventInfo info)> events = new();
            if (receivedEvent != default)
            {
                events.Add(receivedEvent);
            }

            return events;
        }

        /// <inheritdoc/>
        protected override void RemoveReceivedEvent((Event e, EventInfo info) receivedEvent)
        {
            Queue.Remove(receivedEvent);
        }

        /// <inheritdoc/>
        protected override void ClearInbox()
        {
            Queue.Clear();
        }

        /// <inheritdoc/>
        protected override IEnumerable<(Event e, EventInfo info)> GetInboxEvents()
        {
            return Queue;
        }
    }
}