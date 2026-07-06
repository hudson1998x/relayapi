using Relay.Tools;

namespace Relay.Events;

public class ToolCallPendingEventArgs : EventArgs
{
    public PendingToolCall PendingCall { get; }

    public ToolCallPendingEventArgs(PendingToolCall pendingCall)
    {
        PendingCall = pendingCall;
    }
}
