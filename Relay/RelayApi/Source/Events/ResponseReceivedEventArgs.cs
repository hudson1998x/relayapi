using Relay.Abstractions;

namespace Relay.Events;

public class ResponseReceivedEventArgs : EventArgs
{
    public string Response { get; }
    public IReadOnlyList<Message> Conversation { get; }

    public ResponseReceivedEventArgs(string response, IReadOnlyList<Message> conversation)
    {
        Response = response;
        Conversation = conversation;
    }
}
