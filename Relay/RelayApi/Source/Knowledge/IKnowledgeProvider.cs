namespace Relay.Knowledge;

public interface IKnowledgeProvider<TResult>
    where TResult : class, new()
{
    string Name { get; }

    string Description { get; }

    string[] Tags { get; }

    string[] Capabilities { get; }

    ProviderCost Cost { get; }

    Task<TResult> FetchAsync(string query, CancellationToken ct = default);
}
