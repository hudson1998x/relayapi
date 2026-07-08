namespace Relay.Knowledge;

public class KnowledgeQuery
{
    public string Query { get; init; } = string.Empty;

    public string[]? Tags { get; init; }

    public string[]? RequiredCapabilities { get; init; }

    public ProviderCost? MaxCost { get; init; }
}
