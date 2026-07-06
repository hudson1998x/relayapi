# Relay

Relay is a .NET library for communicating with large language models (LLMs) with built-in support for tool calling, approval workflows, personality/response styling, conversation management, and event-driven integrations — all over a pluggable provider interface.

## Features

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

registry.AddTool(
    "weather.get_current",
    typeof(WeatherResult),
    async (string location, string unit) =>
    {
        return new WeatherResult
        {
            Location = location,
            TemperatureCelsius = 22,
            Conditions = "Clear skies"
        };
    },
    true, // RequiresPermission — will be queued for approval
    new ToolArgument(typeof(string), "location"),
    new ToolArgument(typeof(string), "unit", "celsius")
);

// No permission needed — executes instantly
registry.AddTool(
    "math.calculate",
    typeof(double),
    async (double a, double b, string op) => op switch
    {
        "add" => a + b,
        "subtract" => a - b,
        _ => throw new ArgumentException()
    },
    new ToolArgument(typeof(double), "a"),
    new ToolArgument(typeof(double), "b"),
    new ToolArgument(typeof(string), "operation")
);

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

// Listen for tool approval requests (if RequiresPermission is true)
relay.ToolCallPending += (_, e) => e.PendingCall.Approve();

// Send a message with or without a personality
string? reply = await relay.SendMessageAsync("What's the weather in London?");
string? pirated = await relay.SendMessageAsync(
    "What's the weather in London?",
    "You are a cheerful pirate"
);
```

## Tool System

Tools are identified by dot-separated identifiers (`db.create_record`, `http.fetch`, etc.) and support:

| Feature | Description |
|---|---|
| Named arguments | Each parameter has a name, type, and optional default |
| Async handlers | All tool delegates return `Task<T>` |
| Permission control | Set `requiresPermission: true` to gate execution behind approval |
| Structured returns | Complex return types are JSON-serialized with camelCase naming |

### Registration

```csharp
registry.AddTool("identifier", typeof(ReturnType), handler,
    requiresPermission,       // optional, defaults to false
    new ToolArgument(typeof(string), "name"),
    new ToolArgument(typeof(int), "count", 0)
);
```

## Approval Workflow

When the LLM calls a permissioned tool:

1. A `PendingToolCall` is created and added to an internal queue
2. The `ToolCallPending` event fires
3. Execution blocks until `Approve()` or `Reject(reason)` is called
4. If approved, the tool executes and the result is sent back to the LLM
5. If rejected, the rejection reason is sent back as an error

### Polling pattern

```csharp
// From a websocket endpoint or background service:
var pending = relay.GetPendingToolCalls();
// ... send to client, await decision ...
pending.First().Approve();
```

## Personality

Pass a personality as the second argument to `SendMessageAsync`:

```csharp
await relay.SendMessageAsync("Tell me a joke", "You are a grumpy old man");
```

The factual response is generated first (with full tool support), then rephrased according to the personality. If the rephrasing fails, the original response is returned.

## Configuration

```csharp
var config = new RelayConfiguration
{
    MaxConversationHistory = 100,  // trim oldest non-system messages
    OllamaBaseUrl = "http://localhost:11434",
    OllamaModel = "llama3.1"
};
```

## Logging

Four verbosity levels with a pluggable `IRelayLogger` interface:

| Level | Shows |
|---|---|
| `None` | Nothing |
| `Minimal` | Errors, rejections, execution failures |
| `Normal` | Messages, responses, tool calls, JSON results |
| `Verbose` | Raw LLM response dumps |

## Provider Interface

Implement `ILLMProvider` to add any LLM backend:

```csharp
public class MyProvider : ILLMProvider
{
    public async Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken ct)
    {
        // Map request.Messages, request.Tools to your API
        // Return LLMResponse with message and/or tool calls
    }
}
```

## Project Structure

```
RelayApi/Source/
├── Abstractions/        # ILLMProvider, Models (LLMRequest, LLMResponse, Message, ToolCall)
├── Providers/           # OllamaProvider
├── Tools/               # ToolRegistry, ToolDefinition, ToolArgument, PendingToolCall
├── Events/              # ToolCallPendingEventArgs, ResponseReceivedEventArgs
├── Logging/             # LogVerbosity, IRelayLogger
├── Configuration/       # RelayConfiguration
└── Services/            # RelayService
```
