namespace Relay.Knowledge;

public class Procedure
{
    public string Name { get; init; } = string.Empty;

    public string? Category { get; init; }

    public string? Subcategory { get; init; }

    public string[] Tags { get; init; } = [];

    public string[] RequiredTools { get; init; } = [];

    public string[] Steps { get; init; } = [];

    public string[]? Examples { get; init; }
}
