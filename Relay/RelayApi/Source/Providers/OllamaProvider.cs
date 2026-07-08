using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Relay.Abstractions;
using Relay.Logging;
using Relay.Tools;

namespace Relay.Providers;

public class OllamaProvider : ILLMProvider
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly IRelayLogger? _logger;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OllamaProvider(string baseUrl = "http://localhost:11434", string model = "llama3.1", IRelayLogger? logger = null)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _model = model;
        _logger = logger;
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

    public async IAsyncEnumerable<StreamChunk> SendStreamingAsync(
        LLMRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = new
        {
            model = _model,
            stream = true,
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

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = JsonContent.Create(body, options: JsonOpts)
        };

        var response = await _http.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var toolCallsAccumulator = new Dictionary<int, StreamingToolCallAccumulator>();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            _logger?.Log(LogVerbosity.Verbose, $"[Ollama stream] {line}");

            OllamaStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line, JsonOpts);
            }
            catch (JsonException)
            {
                continue;
            }

            if (chunk is null)
                continue;

            if (chunk.Message?.Content is { Length: > 0 })
            {
                yield return new StreamChunk { Content = chunk.Message.Content };
            }

            if (chunk.Message?.ToolCalls is { Count: > 0 })
            {
                foreach (var tc in chunk.Message.ToolCalls)
                {
                    var index = tc.Function?.Index ?? 0;
                    if (!toolCallsAccumulator.TryGetValue(index, out var acc))
                    {
                        acc = new StreamingToolCallAccumulator();
                        toolCallsAccumulator[index] = acc;
                    }

                    if (tc.Function?.Name is { Length: > 0 })
                        acc.Name = tc.Function.Name;

                    if (tc.Function?.Arguments is JsonElement argElement)
                    {
                        if (argElement.ValueKind == JsonValueKind.String)
                            acc.Arguments.Append(argElement.GetString());
                        else
                            acc.Arguments.Append(argElement.GetRawText());
                    }
                }
            }

            if (chunk.Done)
            {
                List<ToolCall>? toolCalls = null;
                if (toolCallsAccumulator.Count > 0)
                {
                    toolCalls = toolCallsAccumulator.Values
                        .Select(acc =>
                        {
                            var argsStr = acc.Arguments.ToString();
                            Dictionary<string, object?> args;
                            if (string.IsNullOrWhiteSpace(argsStr))
                            {
                                args = [];
                            }
                            else
                            {
                                try
                                {
                                    var doc = JsonDocument.Parse(argsStr);
                                    args = doc.RootElement.EnumerateObject()
                                        .ToDictionary(p => p.Name, p => (object?)ConvertJsonElement(p.Value));
                                }
                                catch
                                {
                                    args = [];
                                }
                            }
                            return new ToolCall
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                ToolIdentifier = acc.Name ?? string.Empty,
                                Arguments = args
                            };
                        })
                        .ToList();
                }

                yield return new StreamChunk
                {
                    ToolCalls = toolCalls,
                    IsComplete = true
                };
            }
        }
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

    private class OllamaStreamChunk
    {
        public OllamaStreamMessage? Message { get; set; }
        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }

    private class OllamaStreamMessage
    {
        public string? Role { get; set; }
        public string? Content { get; set; }
        public List<OllamaStreamToolCall>? ToolCalls { get; set; }
    }

    private class OllamaStreamToolCall
    {
        public string? Id { get; set; }
        public OllamaStreamFunction? Function { get; set; }
    }

    private class OllamaStreamFunction
    {
        [JsonPropertyName("index")]
        public int? Index { get; set; }
        public string? Name { get; set; }
        public JsonElement? Arguments { get; set; }
    }

    private class StreamingToolCallAccumulator
    {
        public string? Name { get; set; }
        public System.Text.StringBuilder Arguments { get; } = new();
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
