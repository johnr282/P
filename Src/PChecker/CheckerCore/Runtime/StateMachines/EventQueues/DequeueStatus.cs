// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace PChecker.Runtime.StateMachines.EventQueues
{
    /// <summary>
    /// The status of an event inbox's enabled events.
    /// </summary>
    internal enum EnabledEventsStatus
    {
        /// <summary>
        /// Standard events are enabled.
        /// </summary>
        Success = 0,

        /// <summary>
        /// Only the raised event is enabled.
        /// </summary>
        Raised,

        /// <summary>
        /// Only the default event is enabled.
        /// </summary>
        Default,

        /// <summary>
        /// No events are enabled.
        /// </summary>
        NotAvailable
    }
}