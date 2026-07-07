using System.Collections.Concurrent;
using Relay.Abstractions;
using Relay.Configuration;
using Relay.Events;
using Relay.Logging;
using Relay.Tools;

namespace Relay.Services;

public class RelayService
{
    private readonly ILLMProvider _provider;
    private readonly ToolRegistry _toolRegistry;
    private readonly RelayConfiguration _config;
    private readonly List<Message> _conversation = [];
    private readonly ConcurrentDictionary<Guid, PendingToolCall> _pendingCalls = new();

    public LogVerbosity Verbosity { get; set; } = LogVerbosity.Normal;
    public IRelayLogger? Logger { get; set; }
    public IReadOnlyList<Message> Conversation => _conversation.AsReadOnly();

    public event EventHandler<ToolCallPendingEventArgs>? ToolCallPending;
    public event EventHandler<ResponseReceivedEventArgs>? ResponseReceived;

    public RelayService(ILLMProvider provider, ToolRegistry toolRegistry, RelayConfiguration? config = null)
    {
        _provider = provider;
        _toolRegistry = toolRegistry;
        _config = config ?? new RelayConfiguration();
    }

    public async Task<string?> SendMessageAsync(string message, string? personality = null)
    {
        Log(LogVerbosity.Normal, $"Sending message: {message}");

        _conversation.Add(new Message { Role = "user", Content = message });
        TrimConversation();

        var response = await ProcessConversationAsync();

        if (response is not null && !string.IsNullOrWhiteSpace(personality))
            response = await ApplyPersonalityAsync(response, personality);

        return response;
    }

    private async Task<string?> ProcessConversationAsync()
    {
        var maxIterations = 20;

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            var request = new LLMRequest
            {
                Messages = [.._conversation],
                Tools = _toolRegistry.GetAll().Count > 0 ? [.._toolRegistry.GetAll()] : null
            };

            LLMResponse response;
            try
            {
                response = await _provider.SendAsync(request);
            }
            catch (Exception ex)
            {
                Log(LogVerbosity.Minimal, $"LLM request failed: {ex.Message}");
                throw;
            }

            Log(LogVerbosity.Verbose, $"Raw LLM response: {System.Text.Json.JsonSerializer.Serialize(response)}");

            if (response.ToolCalls is { Count: > 0 })
            {
                var assistantMsg = new Message
                {
                    Role = "assistant",
                    Content = response.Message?.Content,
                    ToolCalls = response.ToolCalls
                };
                _conversation.Add(assistantMsg);

                foreach (var toolCall in response.ToolCalls)
                {
                    var definition = _toolRegistry.GetDefinition(toolCall.ToolIdentifier);

                    if (definition is null)
                    {
                        Log(LogVerbosity.Minimal, $"Unknown tool: {toolCall.ToolIdentifier}");
                        _conversation.Add(new Message
                        {
                            Role = "tool",
                            ToolCallId = toolCall.Id,
                            Content = $"Error: unknown tool '{toolCall.ToolIdentifier}'"
                        });
                        continue;
                    }

                    var positionalArgs = definition.BuildPositionalArgs(toolCall.Arguments);

                    if ((definition.Policy & ToolPolicy.RequiresPermission) != 0)
                    {
                        var pending = new PendingToolCall(toolCall.ToolIdentifier, positionalArgs);
                        _pendingCalls[pending.Id] = pending;

                        Log(LogVerbosity.Normal, $"Tool call pending: {pending.Id} ({toolCall.ToolIdentifier})");
                        ToolCallPending?.Invoke(this, new ToolCallPendingEventArgs(pending));

                        var approval = await pending.WaitForApprovalAsync();

                        _pendingCalls.TryRemove(pending.Id, out _);

                        if (approval == ApprovalResult.Rejected)
                        {
                            var reason = pending.RejectionReason ?? "No reason provided";
                            Log(LogVerbosity.Normal, $"Tool rejected: {pending.Id} ({toolCall.ToolIdentifier}) - {reason}");
                            _conversation.Add(new Message
                            {
                                Role = "tool",
                                ToolCallId = toolCall.Id,
                                Content = $"Error: tool call rejected. Reason: {reason}"
                            });
                            TrimConversation();
                            continue;
                        }

                        Log(LogVerbosity.Normal, $"Tool approved: {pending.Id} ({toolCall.ToolIdentifier})");
                    }

                    try
                    {
                        var result = await definition.ExecuteAsync(positionalArgs);
                        var resultStr = definition.SerializeResult(result);

                        Log(LogVerbosity.Normal, $"Tool result ({toolCall.ToolIdentifier}): {resultStr}");

                        _conversation.Add(new Message
                        {
                            Role = "tool",
                            ToolCallId = toolCall.Id,
                            Content = resultStr
                        });
                    }
                    catch (Exception ex)
                    {
                        Log(LogVerbosity.Minimal, $"Tool execution failed: {toolCall.ToolIdentifier} - {ex.Message}");
                        _conversation.Add(new Message
                        {
                            Role = "tool",
                            ToolCallId = toolCall.Id,
                            Content = $"Error: {ex.Message}"
                        });
                    }

                    TrimConversation();
                }

                continue;
            }

            if (response.Message is not null)
            {
                _conversation.Add(response.Message);
            }

            var responseText = response.Message?.Content ?? string.Empty;
            Log(LogVerbosity.Normal, $"Response received: {responseText}");
            ResponseReceived?.Invoke(this, new ResponseReceivedEventArgs(responseText, _conversation.AsReadOnly()));

            return responseText;
        }

        Log(LogVerbosity.Minimal, "Max conversation iterations reached");
        return null;
    }

    public void ClearConversation()
    {
        _conversation.Clear();
        Log(LogVerbosity.Normal, "Conversation cleared");
    }

    public IReadOnlyList<PendingToolCall> GetPendingToolCalls()
    {
        return _pendingCalls.Values.ToList().AsReadOnly();
    }

    public PendingToolCall? GetPendingToolCall(Guid id)
    {
        _pendingCalls.TryGetValue(id, out var pending);
        return pending;
    }

    private void TrimConversation()
    {
        while (_conversation.Count > _config.MaxConversationHistory)
        {
            var idx = _conversation.FindIndex(m => m.Role != "system");
            if (idx < 0) break;
            _conversation.RemoveAt(idx);
        }
    }

    private async Task<string> ApplyPersonalityAsync(string response, string personality)
    {
        Log(LogVerbosity.Normal, $"Applying personality: {personality}");

        var request = new LLMRequest
        {
            Messages =
            [
                new Message { Role = "system", Content = personality },
                new Message { Role = "user", Content = $"Rephrase the following according to your personality. Only respond with the rephrased text, nothing else.\n\n{response}" }
            ]
        };

        try
        {
            var result = await _provider.SendAsync(request);
            var rephrased = result.Message?.Content ?? response;
            Log(LogVerbosity.Normal, $"Personality response: {rephrased}");
            return rephrased;
        }
        catch (Exception ex)
        {
            Log(LogVerbosity.Minimal, $"Personality processing failed: {ex.Message}");
            return response;
        }
    }

    private void Log(LogVerbosity level, string message)
    {
        if (level <= Verbosity)
            Logger?.Log(level, message);
    }
}
