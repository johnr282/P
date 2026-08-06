// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using PChecker.Runtime.Events;
using PChecker.Runtime.StateMachines.Managers;
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
        protected override IEnumerable<(Event e, EventInfo info)> GetEnabledEventsFromInbox(bool checkOnly = false)
        {
            HashSet<(Event e, EventInfo info)> events = new();
            (Event, EventInfo) nextEvent = TryDequeueEvent(checkOnly);
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

                if (IsEventIgnored(currentEvent.e, currentEvent.info))
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
                if (!IsEventDeferred(currentEvent.e, currentEvent.info))
                {
                    // Cannot remove event from queue yet; scheduler must choose to
                    // execute it first
                    nextAvailableEvent = currentEvent;
                    break;
                }

                node = nextNode;
            }

            return nextAvailableEvent;
        }

        /// <inheritdoc/>
        public override void NotifyChosenEvent(Event e, EventInfo info)
        {
            Queue.Remove((e, info));
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