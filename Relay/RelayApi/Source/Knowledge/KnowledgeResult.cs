namespace Relay.Knowledge;

public class KnowledgeResult
{
    public string Content { get; init; } = string.Empty;

    public string? Source { get; init; }

    public string[]? Tags { get; init; }

    public string Kind { get; init; } = "Data";
}
