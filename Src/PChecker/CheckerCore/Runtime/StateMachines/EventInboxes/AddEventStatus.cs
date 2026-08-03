// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace PChecker.Runtime.StateMachines.EventInboxes
{
    /// <summary>
    /// The status returned as the result of an add event operation.
    /// </summary>
    internal enum AddEventStatus
    {
        /// <summary>
        /// The event handler is already running.
        /// </summary>
        EventHandlerRunning = 0,

        /// <summary>
        /// The event handler is not running.
        /// </summary>
        EventHandlerNotRunning,

        /// <summary>
        /// The event was consumed at a receive statement.
        /// </summary>
        Received,

        /// <summary>
        /// There are no events available to handle.
        /// </summary>
        NoEventsAvailable,

        /// <summary>
        /// The event was dropped.
        /// </summary>
        Dropped
    }
}