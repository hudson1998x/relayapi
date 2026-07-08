namespace Relay.Planning;

public class PlanStep
{
    public int Order { get; init; }

    public string Action { get; init; } = string.Empty;

    public string? ToolIdentifier { get; init; }

    public Dictionary<string, object?>? Arguments { get; init; }

    public string? Description { get; init; }
}
