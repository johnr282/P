namespace PChecker.Configuration;

/// <summary>
/// Inbox types for P state machines
/// </summary>
public enum InboxType
{
    /// <summary>
    /// Inbox where events are delivered in FIFO order
    /// </summary>
    EventQueue,

    /// <summary>
    /// Inbox where events can be delivered in any order
    /// </summary>
    EventSet
}