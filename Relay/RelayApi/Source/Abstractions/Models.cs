using Relay.Tools;

namespace Relay.Abstractions;

public class LLMRequest
{
    public List<Message> Messages { get; set; } = [];
    public List<ToolDefinition>? Tools { get; set; }
}

public class LLMResponse
{
    public Message? Message { get; set; }
    public List<ToolCall>? ToolCalls { get; set; }
}

public class Message
{
    public string Role { get; set; } = string.Empty;
    public string? Content { get; set; }
    public List<ToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
}

public class ToolCall
{
    public string Id { get; set; } = string.Empty;
    public string ToolIdentifier { get; set; } = string.Empty;
    public Dictionary<string, object?> Arguments { get; set; } = [];
}
