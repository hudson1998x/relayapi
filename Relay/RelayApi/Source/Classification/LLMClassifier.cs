using System.Text.Json;
using Relay.Abstractions;

namespace Relay.Classification;

public class LLMClassifier : IKnowledgeClassifier
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILLMProvider _provider;
    private readonly string _systemPrompt;

    public LLMClassifier(ILLMProvider provider, string? systemPrompt = null)
    {
        _provider = provider;
        _systemPrompt = systemPrompt ?? DefaultSystemPrompt;
    }

    public static string DefaultSystemPrompt { get; } = """
        You are a query classifier. Determine whether the user's message requires retrieving external knowledge.
        
        Return a JSON object:
        - category: broad domain (Development, Filesystem, General, Database, Database, Image, Document)
        - subcategory: specific area (C#, PHP, Navigation, Debugging, Querying, etc. or null)
        - knowledgeRequired: true if answering requires facts, file lookups, code navigation, docs, or tools
        - relevantTags: array of short tags describing the topic (e.g. ["csharp","classes","navigation"])
        - confidence: 0.0 to 1.0
        
        Rules:
        - Follow-up questions like "What about X" or "and X?" inherit context from the conversation
        - ANY technical topic (code, files, databases, images, documents) must set knowledgeRequired=true
        - When uncertain, default to knowledgeRequired=true
        - "General" is only for casual chat, greetings, or questions with no factual answer needed
        
        Examples:
        User: Where can I find the UserProvider class?
        Response: {"category":"Development","subcategory":"C#","knowledgeRequired":true,"relevantTags":["csharp","classes","navigation","source-code"],"confidence":0.95}
        
        User: What about Relay.sln?
        Conversation: previous question about finding a solution file
        Response: {"category":"Filesystem","subcategory":"Navigation","knowledgeRequired":true,"relevantTags":["file","navigation","search"],"confidence":0.9}
        
        User: Hello, how are you?
        Response: {"category":"General","subcategory":null,"knowledgeRequired":false,"relevantTags":[],"confidence":1.0}
        
        User: Fix the PHP syntax error on line 45
        Response: {"category":"Development","subcategory":"PHP","knowledgeRequired":true,"relevantTags":["php","debugging","syntax"],"confidence":0.95}
        """;

    public async Task<ClassificationResult> ClassifyAsync(string message, IReadOnlyList<Message>? conversation = null, CancellationToken ct = default)
    {
        var userContent = BuildUserMessage(message, conversation);

        var request = new LLMRequest
        {
            Messages =
            [
                new Message { Role = "system", Content = _systemPrompt },
                new Message { Role = "user", Content = userContent }
            ]
        };

        try
        {
            var response = await _provider.SendAsync(request, ct);
            var content = response.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
                return DefaultResult();

            var json = ExtractJson(content);
            if (json is null)
                return DefaultResult();

            var result = JsonSerializer.Deserialize<ClassificationResult>(json, JsonOpts);
            return result ?? DefaultResult();
        }
        catch
        {
            return DefaultResult();
        }
    }

    private static string BuildUserMessage(string message, IReadOnlyList<Message>? conversation)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Classify this message:");
        sb.AppendLine(message);

        if (conversation is { Count: > 0 })
        {
            var recent = conversation
                .Where(m => m.Role is "user" or "assistant" or "tool")
                .TakeLast(6)
                .ToList();

            if (recent.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Recent conversation context:");
                foreach (var msg in recent)
                {
                    var preview = msg.Content is not null && msg.Content.Length > 200
                        ? msg.Content[..200] + "..."
                        : msg.Content ?? "";
                    sb.AppendLine($"[{msg.Role}]: {preview}");
                }
            }
        }

        return sb.ToString();
    }

    private static ClassificationResult DefaultResult() => new()
    {
        Category = "General",
        KnowledgeRequired = false,
        Confidence = 0.0
    };

    private static string? ExtractJson(string text)
    {
        var startMarker = "```json";
        var start = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start += startMarker.Length;
            var end = text.IndexOf("```", start, StringComparison.OrdinalIgnoreCase);
            if (end > start)
                return text[start..end].Trim();
        }

        start = text.IndexOf('{');
        if (start >= 0)
        {
            var end = text.LastIndexOf('}');
            if (end > start)
                return text[start..(end + 1)];
        }

        return null;
    }
}
