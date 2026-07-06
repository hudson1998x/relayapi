# Listening to Events

Relay exposes events for tool approval requests and responses. These allow external systems (e.g., a websocket API) to observe and interact with the service.

## Tool Approval Flow

When the LLM decides to call a tool, a `PendingToolCall` is created and the `ToolCallPending` event fires. Execution is **blocked** until the pending call is approved or rejected.

### Event-based approval

```csharp
relay.ToolCallPending += OnToolCallPending;

void OnToolCallPending(object? sender, ToolCallPendingEventArgs e)
{
    var call = e.PendingCall;

    Console.WriteLine($"Tool call: {call.ToolIdentifier}");
    Console.WriteLine($"  Id: {call.Id}");
    Console.WriteLine($"  Args: {string.Join(", ", call.Arguments)}");

    // Approve or reject
    if (ShouldApprove(call))
        call.Approve();
    else
        call.Reject("User declined this operation");
}
```

### Polling-based approval (websocket API pattern)

```csharp
// In a background loop or API endpoint:
var pending = relay.GetPendingToolCalls();
foreach (var call in pending)
{
    // Send to websocket clients for manual approval
    BroadcastToClients(new
    {
        type = "tool_pending",
        id = call.Id,
        tool = call.ToolIdentifier,
        args = call.Arguments
    });
}

// When the websocket client responds:
relay.GetPendingToolCall(guid)?.Approve();    // or .Reject("reason")
```

## Receiving Responses

The `ResponseReceived` event fires when the LLM produces a final response (after all tool calls complete):

```csharp
relay.ResponseReceived += (_, e) =>
{
    Console.WriteLine($"Response: {e.Response}");

    // The full conversation history is also available
    foreach (var msg in e.Conversation)
    {
        Console.WriteLine($"[{msg.Role}] {msg.Content}");
    }
};
```

## Accessing Conversation History

```csharp
// Read-only snapshot of the current conversation
IReadOnlyList<Message> history = relay.Conversation;

foreach (var msg in history)
{
    Console.WriteLine($"{msg.Role}: {msg.Content}");
}

// Clear when starting a new topic
relay.ClearConversation();
```

## Thread Safety

Events fire on the thread executing `SendMessageAsync`. If you need to dispatch to a specific synchronization context (e.g., a UI thread or websocket send loop), handle that in your event handler.

## Full Example

```csharp
var relay = new RelayService(provider, registry);

// Event-based
relay.ToolCallPending += (_, e) =>
{
    Console.WriteLine($"Pending: {e.PendingCall.ToolIdentifier}");
    e.PendingCall.Approve();
};

relay.ResponseReceived += (_, e) =>
{
    Console.WriteLine($"Final response: {e.Response}");
};

// Also allow queue-based polling from another component
// relay.GetPendingToolCalls() can be called from a web API endpoint
```
