// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace PChecker.Runtime.StateMachines.EventInboxes
{
    /// <summary>
    /// The status of an event inbox.
    /// </summary>
    internal enum InboxStatus
    {
        /// <summary>
        /// Events are enabled.
        /// </summary>
        EventsEnabled = 0,

        /// <summary>
        /// An event has been raised.
        /// </summary>
        Raised,

        /// <summary>
        /// Only the default event is enabled.
        /// </summary>
        Default,

        /// <summary>
        /// No events are enabled.
        /// </summary>
        NoEventsEnabled
    }
}