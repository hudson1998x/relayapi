# Relay

Relay is a .NET library for communicating with large language models (LLMs) with built-in support for streaming, tool calling, approval workflows, personality/response styling, conversation management, and event-driven integrations — all over a pluggable provider interface.

## Features

- **Streaming mode** — Real-time token delivery via `IAsyncEnumerable`. Streaming pauses automatically when tool calls are detected, executes them, then resumes.
- **Tool system** — Register typed tools with named arguments and async delegates. Tools can require explicit approval or execute automatically.
- **Approval workflow** — Pending tool calls are queued and await approval. Supports both event-driven and polling-based patterns for integration with websocket APIs or other external approval UIs.
- **Personality per prompt** — Pass a personality description per message to rephrase responses (e.g., "You are a cheerful pirate", "Speak like a grumpy old man").
- **Structured results** — Tool return values are serialized as strict JSON matching their schema, giving the LLM clean structured data.
- **Configurable logging** — Four verbosity levels with a pluggable logger interface.
- **Pluggable provider** — Built-in Ollama provider; implement `ILLMProvider` to add OpenAI, Anthropic, or any other backend.
- **Conversation management** — Automatic history trimming, programmatic access to the full conversation, and the ability to reset.

## Quick Start

```csharp
using Relay.Abstractions;
using Relay.Configuration;
using Relay.Logging;
using Relay.Providers;
using Relay.Services;
using Relay.Tools;

// Configure
var config = new RelayConfiguration
{
    OllamaModel = "llama3.1"
};

// Register tools
var registry = new ToolRegistry();

// Requires explicit approval
registry.AddTool(
    "weather.get_current",
    typeof(WeatherResult),
    async (string location, string unit) =>
    {
        await Task.Delay(100);
        return new WeatherResult
        {
            Location = location,
            TemperatureCelsius = 22,
            Conditions = "Clear skies"
        };
    },
    ToolPolicy.RequiresPermission,
    "Get the current weather for a location",
    null,
    new ToolArgument(typeof(string), "location"),
    new ToolArgument(typeof(string), "unit", "celsius")
);

// No permission needed — executes instantly (default policy)
registry.AddTool(
    "math.calculate",
    typeof(double),
    async (double a, double b, string operation) =>
    {
        await Task.Delay(50);
        return operation switch
        {
            "add" => a + b,
            "subtract" => a - b,
            "multiply" => a * b,
            "divide" => a / b,
            _ => throw new ArgumentException($"Unknown operation: {operation}")
        };
    },
    "Perform mathematical calculations",
    "Use 'add' for addition, 'subtract' for subtraction, 'multiply' for multiplication, 'divide' for division, and 'square root' or 'sqrt' for square root",
    new ToolArgument(typeof(double), "a"),
    new ToolArgument(typeof(double), "b"),
    new ToolArgument(typeof(string), "operation")
);

// Override policy after registration
registry.ChangePolicy("math.calculate", ToolPolicy.RequiresPermission);

// Create the service
var relay = new RelayService(
    new OllamaProvider("http://localhost:11434", "llama3.1"),
    registry,
    config
)
{
    Verbosity = LogVerbosity.Normal,
    Logger = new ConsoleLogger()
};

// Listen for tool approval requests (if RequiresPermission is set)
relay.ToolCallPending += (sender, e) =>
{
    Console.WriteLine($"Approving tool: {e.PendingCall.ToolIdentifier}");
    e.PendingCall.Approve();
};

// Non-streaming
string? reply = await relay.SendMessageAsync("What's the weather in London?");

// Streaming
await foreach (var chunk in relay.SendMessageStreamingAsync("What's the weather in London?"))
{
    if (chunk.Content is not null)
        Console.Write(chunk.Content);
}
```

## Streaming

Streaming mode delivers tokens as they are generated using `IAsyncEnumerable<StreamChunk>`. When the LLM invokes a tool during a streaming response, the stream pauses, the tool executes (with the existing approval workflow), and the stream automatically resumes with the next response.

### Basic usage

```csharp
await foreach (var chunk in relay.SendMessageStreamingAsync("Explain quantum computing"))
{
    if (chunk.Content is not null)
        Console.Write(chunk.Content);
}
```

### With personality

```csharp
await foreach (var chunk in relay.SendMessageStreamingAsync(
    "Explain quantum computing",
    "You are a pirate"))
{
    if (chunk.Content is not null)
        Console.Write(chunk.Content);
}
```

### With cancellation

```csharp
using var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(30));

try
{
    await foreach (var chunk in relay.SendMessageStreamingAsync(
        "Tell me a story",
        ct: cts.Token))
    {
        if (chunk.Content is not null)
            Console.Write(chunk.Content);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("\n[Timed out]");
}
```

### Lifecycle events

```csharp
relay.StreamingResponseReceived += (sender, e) =>
{
    if (e.IsToolCallPause)
        Console.WriteLine("\n[Executing tool calls...]");
    else if (e.IsComplete)
        Console.WriteLine("\n[Stream complete]");
};
```

### StreamChunk

Each chunk yielded by the stream contains:

| Property | Description |
|---|---|
| `Content` | A text token from the LLM (null on tool-call chunks) |
| `ToolCalls` | Completed tool calls when the stream finishes with tool invocations (null on content chunks) |
| `IsComplete` | `true` on the final chunk of a streaming response |

### How tool calls work during streaming

When the LLM decides to call a tool mid-stream:

1. The content tokens up to that point are yielded to the caller
2. A chunk with `ToolCalls` and `IsComplete = true` is yielded
3. `RelayService` executes the tool (with approval if required)
4. The tool result is added to the conversation
5. A new streaming request is sent to the LLM
6. The new response tokens are yielded to the caller as a seamless continuation

The consumer sees a continuous stream of tokens across multiple LLM round-trips without any extra code.

## Tool System

Tools are identified by dot-separated identifiers (`db.create_record`, `http.fetch`, etc.) and support:

| Feature | Description |
|---|---|
| Named arguments | Each parameter has a name, type, and optional default |
| Async handlers | All tool delegates return `Task<T>` |
| Permission control | Set `ToolPolicy.RequiresPermission` to gate execution behind approval |
| Policy override | `ChangePolicy()` updates a tool's permission policy after registration |
| Structured returns | Complex return types are JSON-serialized with camelCase naming |

### Registration

```csharp
var registry = new ToolRegistry();

// With permission, description, and usage instructions
registry.AddTool(
    "identifier",
    typeof(ReturnType),
    handler,
    ToolPolicy.RequiresPermission,
    "Description of what the tool does",
    "Instructions for the LLM on how to invoke this tool",
    new ToolArgument(typeof(string), "name"),
    new ToolArgument(typeof(int), "count", 0)
);

// Without permission (default)
registry.AddTool(
    "identifier",
    typeof(ReturnType),
    handler,
    "Description",
    null,
    new ToolArgument(typeof(string), "name")
);

// Override after registration
registry.ChangePolicy("identifier", ToolPolicy.RequiresPermission);
```

### Description and Usage

Each tool can include a **description** and **usage instructions** sent directly to the LLM:

- **Description** — a short summary of what the tool does, helping the LLM decide which tool to call
- **Usage** — guidance on how to invoke the tool correctly (e.g., "Use 'square root' or 'sqrt' as the operation for square root calculations")

When no description is provided, the LLM receives an empty description. Both fields are combined and sent in the `description` field of the function definition:

```
Perform mathematical calculations

Usage: Use 'add' for addition, 'subtract' for subtraction, 'multiply' for multiplication, 'divide' for division, and 'square root' or 'sqrt' for square root. Square root only uses parameter 'a'.
```

## ToolPolicy

The `ToolPolicy` enum is a `[Flags]` bit mask that controls tool behavior:

| Value                | Meaning                                           |
|----------------------|---------------------------------------------------|
| `None`               | No special behavior (default)                     |
| `RequiresPermission` | Tool call is queued for approval before execution |

Policies are combined with bitwise OR, making the system extensible for future flags.

## Approval Workflow

When the LLM calls a permissioned tool:

1. A `PendingToolCall` is created and added to an internal queue
2. The `ToolCallPending` event fires
3. Execution blocks until `Approve()` or `Reject(reason)` is called
4. If approved, the tool executes and the result is sent back to the LLM
5. If rejected, the rejection reason is sent back as an error

This works identically in both streaming and non-streaming modes.

### Event-driven

```csharp
relay.ToolCallPending += (sender, e) =>
{
    Console.WriteLine($"Tool: {e.PendingCall.ToolIdentifier}");
    Console.WriteLine($"Arguments: {string.Join(", ", e.PendingCall.Arguments)}");
    e.PendingCall.Approve();
};
```

### Polling pattern

```csharp
var pending = relay.GetPendingToolCalls();
foreach (var call in pending)
{
    Console.WriteLine($"Tool: {call.ToolIdentifier} (id: {call.Id})");
    call.Approve();
}
```

### Manual approval and rejection

```csharp
relay.ToolCallPending += (sender, e) =>
{
    var call = e.PendingCall;

    // Approve
    call.Approve();

    // Or reject with a reason
    call.Reject("Not authorized to perform this action");
};
```

## Personality

Pass a personality as the second argument to `SendMessageAsync` or `SendMessageStreamingAsync`:

```csharp
// Non-streaming
string? reply = await relay.SendMessageAsync(
    "Tell me a joke",
    "You are a grumpy old man"
);

// Streaming
await foreach (var chunk in relay.SendMessageStreamingAsync(
    "Tell me a joke",
    "You are a grumpy old man"))
{
    if (chunk.Content is not null)
        Console.Write(chunk.Content);
}
```

The factual response is generated first (with full tool support), then rephrased according to the personality. If the rephrasing fails, the original response is returned.

## Configuration

```csharp
var config = new RelayConfiguration
{
    MaxConversationHistory = 100,
    OllamaBaseUrl = "http://localhost:11434",
    OllamaModel = "llama3.1"
};
```

| Property | Default | Description |
|---|---|---|
| `MaxConversationHistory` | `50` | Maximum messages to keep. Oldest non-system messages are trimmed. |
| `OllamaBaseUrl` | `"http://localhost:11434"` | Base URL for the Ollama API. |
| `OllamaModel` | `"llama3.1"` | Model name to use with Ollama. |

## Logging

Four verbosity levels with a pluggable `IRelayLogger` interface:

```csharp
public interface IRelayLogger
{
    void Log(LogVerbosity level, string message);
}
```

| Level     | Shows                                         |
|-----------|-----------------------------------------------|
| `None`    | Nothing                                       |
| `Minimal` | Errors, rejections, execution failures        |
| `Normal`  | Messages, responses, tool calls, JSON results |
| `Verbose` | Raw LLM response dumps                        |

### Example logger

```csharp
public class ConsoleLogger : IRelayLogger
{
    public void Log(LogVerbosity level, string message)
    {
        var prefix = level switch
        {
            LogVerbosity.Minimal => "[ERR]",
            LogVerbosity.Normal => "[INF]",
            LogVerbosity.Verbose => "[DBG]",
            _ => "[?]"
        };
        Console.WriteLine($"{prefix} {message}");
    }
}
```

## Provider Interface

Implement `ILLMProvider` to add any LLM backend:

```csharp
public interface ILLMProvider
{
    Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken ct = default);
    IAsyncEnumerable<StreamChunk> SendStreamingAsync(LLMRequest request, CancellationToken ct = default);
}
```

### Implementing a non-streaming provider

```csharp
public class MyProvider : ILLMProvider
{
    public async Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken ct = default)
    {
        // Map request.Messages and request.Tools to your API
        // Return an LLMResponse with a Message and/or ToolCalls
        return new LLMResponse
        {
            Message = new Message { Role = "assistant", Content = "..." }
        };
    }

    public async IAsyncEnumerable<StreamChunk> SendStreamingAsync(
        LLMRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Stream tokens as they arrive
        yield return new StreamChunk { Content = "token" };

        // When done, yield the final chunk
        yield return new StreamChunk { IsComplete = true };
    }
}
```

### Implementing a streaming provider

When implementing `SendStreamingAsync`, the method should:

1. Send a request to the LLM with streaming enabled
2. Read response chunks incrementally
3. `yield return new StreamChunk { Content = "token" }` for each text token
4. Accumulate tool call arguments across chunks (they arrive incrementally from the LLM)
5. `yield return new StreamChunk { ToolCalls = [...], IsComplete = true }` when the stream ends

## Events

| Event | Args | Fires when |
|---|---|---|
| `ToolCallPending` | `ToolCallPendingEventArgs` | A permissioned tool call is queued for approval |
| `ResponseReceived` | `ResponseReceivedEventArgs` | A non-streaming response is fully received |
| `StreamingResponseReceived` | `StreamingResponseReceivedEventArgs` | A streaming response completes or pauses for tool execution |

### ToolCallPendingEventArgs

| Property | Type | Description |
|---|---|---|
| `PendingCall` | `PendingToolCall` | The pending tool call awaiting approval |

### ResponseReceivedEventArgs

| Property | Type | Description |
|---|---|---|
| `Response` | `string` | The full response text |
| `Conversation` | `IReadOnlyList<Message>` | Current conversation history |

### StreamingResponseReceivedEventArgs

| Property | Type | Description |
|---|---|---|
| `AccumulatedResponse` | `string` | The full response text accumulated so far |
| `Conversation` | `IReadOnlyList<Message>` | Current conversation history |
| `IsToolCallPause` | `bool` | `true` if the stream paused for tool execution |
| `IsComplete` | `bool` | `true` if the streaming response is fully done |

## Models

### LLMRequest

```csharp
public class LLMRequest
{
    public List<Message> Messages { get; set; }
    public List<ToolDefinition>? Tools { get; set; }
}
```

### LLMResponse

```csharp
public class LLMResponse
{
    public Message? Message { get; set; }
    public List<ToolCall>? ToolCalls { get; set; }
}
```

### Message

```csharp
public class Message
{
    public string Role { get; set; }
    public string? Content { get; set; }
    public List<ToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
}
```

### ToolCall

```csharp
public class ToolCall
{
    public string Id { get; set; }
    public string ToolIdentifier { get; set; }
    public Dictionary<string, object?> Arguments { get; set; }
}
```

### StreamChunk

```csharp
public class StreamChunk
{
    public string? Content { get; set; }
    public List<ToolCall>? ToolCalls { get; set; }
    public bool IsComplete { get; set; }
}
```

## Project Structure

```
RelayApi/Source/
├── Abstractions/        # ILLMProvider, Models (LLMRequest, LLMResponse, Message, ToolCall, StreamChunk)
├── Providers/           # OllamaProvider
├── Tools/               # ToolRegistry, ToolDefinition, ToolArgument, PendingToolCall, ToolPolicy
├── Events/              # ToolCallPendingEventArgs, ResponseReceivedEventArgs, StreamingResponseReceivedEventArgs
├── Logging/             # LogVerbosity, IRelayLogger
├── Configuration/       # RelayConfiguration
├── Knowledge/           # IKnowledgeProvider, KnowledgeProviderRegistry, KnowledgeQuery, KnowledgeResult
├── Classification/      # IKnowledgeClassifier, LLMClassifier
├── Planning/            # IPlanner, LLMPlanner, Plan, PlanStep
└── Services/            # RelayService
```
