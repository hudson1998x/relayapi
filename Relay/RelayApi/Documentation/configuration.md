# Configuration

`RelayConfiguration` controls conversation limits and provider defaults.

## Options

| Property | Type | Default | Description |
|---|---|---|---|
| `MaxConversationHistory` | `int` | `50` | Max messages kept in history before trimming (oldest non-system messages are removed) |
| `OllamaBaseUrl` | `string` | `"http://localhost:11434"` | Base URL for the Ollama API |
| `OllamaModel` | `string` | `"llama3.1"` | Default model to use |

## Usage

```csharp
var config = new RelayConfiguration
{
    MaxConversationHistory = 100,
    OllamaBaseUrl = "http://192.168.1.50:11434",
    OllamaModel = "mistral"
};

var relay = new RelayService(provider, registry, config);
```

## Conversation Trimming

When the conversation history exceeds `MaxConversationHistory`, Relay trims the oldest non-system messages. System messages (typically used for personality/instruction prompts) are preserved.

To manually manage the conversation:

```csharp
// Clear entirely
relay.ClearConversation();

// Or interact with the conversation directly
var history = relay.Conversation;
```

## Provider Configuration

The `OllamaProvider` accepts direct constructor arguments that take precedence over `RelayConfiguration`:

```csharp
// Using configuration
var provider = new OllamaProvider(config.OllamaBaseUrl, config.OllamaModel);

// Explicit override
var provider = new OllamaProvider("http://localhost:11434", "llama3.1:70b");
```
