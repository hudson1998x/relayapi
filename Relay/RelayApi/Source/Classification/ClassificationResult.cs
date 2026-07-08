using System.Text.Json.Serialization;

namespace Relay.Classification;

public class ClassificationResult
{
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("subcategory")]
    public string? Subcategory { get; init; }

    [JsonPropertyName("knowledgeRequired")]
    public bool KnowledgeRequired { get; init; }

    [JsonPropertyName("relevantTags")]
    public string[] RelevantTags { get; init; } = [];

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }
}
