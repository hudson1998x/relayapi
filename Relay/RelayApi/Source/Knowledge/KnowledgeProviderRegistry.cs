using System.Collections.Concurrent;
using System.Text.Json;

namespace Relay.Knowledge;

public class KnowledgeProviderRegistry
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ConcurrentDictionary<string, ProviderEntry> _providers = new(StringComparer.OrdinalIgnoreCase);

    public void AddProvider<TProvider, TResult>(TProvider provider)
        where TProvider : class, IKnowledgeProvider<TResult>
        where TResult : class, new()
    {
        var entry = new ProviderEntry<TResult>(provider);
        _providers[provider.Name] = entry;
    }

    public async Task<IReadOnlyList<KnowledgeResult>> QueryAsync(KnowledgeQuery query, CancellationToken ct = default)
    {
        var candidates = _providers.Values
            .Where(e => MatchesQuery(e, query))
            .OrderBy(e => e.Cost)
            .ToList();

        var results = new List<KnowledgeResult>();

        foreach (var entry in candidates)
        {
            var result = await entry.FetchAsync(query.Query, ct);
            results.Add(result);
        }

        return results;
    }

    public IReadOnlyList<ProviderInfo> GetProviders()
    {
        return _providers.Values.Select(e => new ProviderInfo(e.Name, e.Description, e.Tags, e.Capabilities, e.Cost)).ToList();
    }

    private static bool MatchesQuery(ProviderEntry entry, KnowledgeQuery query)
    {
        if (query.RequiredCapabilities is { Length: > 0 })
        {
            var caps = entry.Capabilities ?? [];
            if (!query.RequiredCapabilities.Any(c => caps.Contains(c, StringComparer.OrdinalIgnoreCase)))
                return false;
        }

        if (query.Tags is { Length: > 0 })
        {
            var tags = entry.Tags ?? [];
            if (!query.Tags.Any(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                return false;
        }

        if (query.MaxCost.HasValue && entry.Cost > query.MaxCost.Value)
            return false;

        return true;
    }

    private abstract class ProviderEntry
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract string[] Tags { get; }
        public abstract string[] Capabilities { get; }
        public abstract ProviderCost Cost { get; }
        public abstract Task<KnowledgeResult> FetchAsync(string query, CancellationToken ct);
    }

    private sealed class ProviderEntry<TResult> : ProviderEntry
        where TResult : class, new()
    {
        private readonly IKnowledgeProvider<TResult> _provider;

        public ProviderEntry(IKnowledgeProvider<TResult> provider)
        {
            _provider = provider;
        }

        public override string Name => _provider.Name;
        public override string Description => _provider.Description;
        public override string[] Tags => _provider.Tags;
        public override string[] Capabilities => _provider.Capabilities;
        public override ProviderCost Cost => _provider.Cost;

        public override async Task<KnowledgeResult> FetchAsync(string query, CancellationToken ct)
        {
            var result = await _provider.FetchAsync(query, ct);

            var kind = DetectKind(result);
            var content = kind == "Data" && result is string s
                ? s
                : JsonSerializer.Serialize(result, JsonOpts);

            return new KnowledgeResult
            {
                Content = content,
                Source = _provider.Name,
                Tags = _provider.Tags,
                Kind = kind
            };
        }

        private static string DetectKind(object? result)
        {
            return result switch
            {
                Procedure _ => "Procedure",
                List<Procedure> _ => "Procedure",
                _ => "Data"
            };
        }
    }
}

public record ProviderInfo(string Name, string Description, string[] Tags, string[] Capabilities, ProviderCost Cost);
