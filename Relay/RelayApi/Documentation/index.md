# Relay — LLM Communication Library

Relay is a .NET library for communicating with large language models (LLMs) with built-in support for tool calling, approval workflows, conversation management, and event-driven integrations.

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
    typeof(string),
    async (string location) => $"Weather in {location} is 22°C",
    new ToolArgument(typeof(string), "location")
);

// Create provider and service
var provider = new OllamaProvider("http://localhost:11434", "llama3.1");
var relay = new RelayService(provider, registry, config)
{
    Logger = new ConsoleLogger()
};

// Listen for tool approval requests
relay.ToolCallPending += (_, e) => e.PendingCall.Approve();

// Send a message
string? response = await relay.SendMessageAsync("What's the weather in London?");
Console.WriteLine(response);
```

## Core Concepts

| Component | Description |
|---|---|---|
| `RelayService` | Main entrypoint — manages conversation, tools, and events |
| `ILLMProvider` | Abstraction over LLM backends (Ollama built-in) |
| `ToolRegistry` | Collection of registered tool definitions |
| `ToolPolicy` | Bit-flag enum controlling tool permissions (e.g., `RequiresPermission`) |
| `PendingToolCall` | A tool call awaiting approval before execution |
| `IRelayLogger` | Pluggable logger with verbosity levels |

## Getting Started

- [Adding Tools](adding-tools.md) — Register and implement tool calls
- [Listening to Events](listening.md) — Approve tools, receive responses
- [Configuration](configuration.md) — All configuration options
- [Logging](logging.md) — Verbosity levels and custom loggers
