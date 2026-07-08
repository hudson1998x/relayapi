using Relay.Abstractions;

namespace Relay.Events;

public class StreamingResponseReceivedEventArgs : EventArgs
{
    public string AccumulatedResponse { get; }
    public IReadOnlyList<Message> Conversation { get; }
    public bool IsToolCallPause { get; }
    public bool IsComplete { get; }

    public StreamingResponseReceivedEventArgs(
        string accumulatedResponse,
        IReadOnlyList<Message> conversation,
        bool isToolCallPause,
        bool isComplete)
    {
        AccumulatedResponse = accumulatedResponse;
        Conversation = conversation;
        IsToolCallPause = isToolCallPause;
        IsComplete = isComplete;
    }
}
