using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Relay.Abstractions;
using Relay.Tools;

namespace Relay.Providers;

public class OllamaProvider : ILLMProvider
{
    private readonly HttpClient _http;
    private readonly string _model;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OllamaProvider(string baseUrl = "http://localhost:11434", string model = "llama3.1")
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _model = model;
    }

    public async Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken ct = default)
    {
        var body = new
        {
            model = _model,
            stream = false,
            messages = request.Messages.Select(m => new
            {
                role = m.Role,
                content = m.Content,
                tool_calls = m.ToolCalls?.Select(tc => new
                {
                    function = new
                    {
                        name = tc.ToolIdentifier,
                        arguments = tc.Arguments
                    }
                }).ToList(),
                tool_call_id = m.ToolCallId
            }).ToList(),
            tools = request.Tools?.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Identifier,
                    description = BuildToolDescription(t.Description, t.Usage),
                    parameters = BuildParameters(t.Arguments)
                }
            }).ToList()
        };

        var response = await _http.PostAsJsonAsync("api/chat", body, JsonOpts, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<OllamaResponse>(JsonOpts, ct);
        if (json is null)
            return new LLMResponse();

        var result = new LLMResponse
        {
            Message = new Message
            {
                Role = json.Message?.Role ?? "assistant",
                Content = json.Message?.Content
            }
        };

        if (json.Message?.ToolCalls is { Count: > 0 })
        {
            result.ToolCalls = json.Message.ToolCalls.Select(tc => new ToolCall
            {
                Id = tc.Function?.Name is not null ? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N"),
                ToolIdentifier = tc.Function?.Name ?? string.Empty,
                Arguments = ParseArguments(tc.Function?.Arguments)
            }).ToList();
        }

        return result;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static object BuildParameters(IReadOnlyList<ToolArgument> arguments)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var arg in arguments)
        {
            properties[arg.Name] = new { type = MapType(arg.Type) };
            required.Add(arg.Name);
        }

        return new
        {
            type = "object",
            properties,
            required
        };
    }

    private static string BuildToolDescription(string? description, string? usage)
    {
        if (string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(usage))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(description))
            return $"Usage: {usage}";

        if (string.IsNullOrWhiteSpace(usage))
            return description;

        return $"{description}\n\nUsage: {usage}";
    }

    private static string MapType(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return "number";
        if (type == typeof(bool)) return "boolean";
        return "string";
    }

    private class OllamaResponse
    {
        public OllamaMessage? Message { get; set; }
    }

    private class OllamaMessage
    {
        public string? Role { get; set; }
        public string? Content { get; set; }
        public List<OllamaToolCall>? ToolCalls { get; set; }
    }

    private class OllamaToolCall
    {
        public OllamaFunction? Function { get; set; }
    }

    private class OllamaFunction
    {
        public string? Name { get; set; }
        public JsonElement? Arguments { get; set; }
    }

    private static Dictionary<string, object?> ParseArguments(JsonElement? argumentsElement)
    {
        if (argumentsElement is not JsonElement element)
            return [];

        Dictionary<string, JsonElement>? dict = null;

        if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString();
            if (string.IsNullOrWhiteSpace(str))
                return [];
            try { dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(str); }
            catch { return []; }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            try { dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(element.GetRawText()); }
            catch { return []; }
        }

        if (dict is null)
            return [];

        return dict.ToDictionary(
            kvp => kvp.Key,
            kvp => (object?)ConvertJsonElement(kvp.Value));
    }
}
