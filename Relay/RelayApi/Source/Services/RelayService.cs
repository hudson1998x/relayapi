using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Relay.Abstractions;
using Relay.Classification;
using Relay.Configuration;
using Relay.Events;
using Relay.Knowledge;
using Relay.Logging;
using Relay.Planning;
using Relay.Tools;

namespace Relay.Services;

public class RelayService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILLMProvider _provider;
    private readonly ToolRegistry _toolRegistry;
    private readonly RelayConfiguration _config;
    private readonly List<Message> _conversation = [];
    private readonly ConcurrentDictionary<Guid, PendingToolCall> _pendingCalls = new();
    private readonly IKnowledgeClassifier? _classifier;
    private readonly KnowledgeProviderRegistry? _knowledgeRegistry;
    private readonly IPlanner? _planner;
    private readonly KnowledgeFormatter _formatter = new();

    public LogVerbosity Verbosity { get; set; } = LogVerbosity.Normal;
    public IRelayLogger? Logger { get; set; }
    public IReadOnlyList<Message> Conversation => _conversation.AsReadOnly();

    public event EventHandler<ToolCallPendingEventArgs>? ToolCallPending;
    public event EventHandler<ResponseReceivedEventArgs>? ResponseReceived;
    public event EventHandler<StreamingResponseReceivedEventArgs>? StreamingResponseReceived;

    public RelayService(
        ILLMProvider provider,
        ToolRegistry toolRegistry,
        RelayConfiguration? config = null,
        IKnowledgeClassifier? classifier = null,
        KnowledgeProviderRegistry? knowledgeRegistry = null,
        IPlanner? planner = null)
    {
        _provider = provider;
        _toolRegistry = toolRegistry;
        _config = config ?? new RelayConfiguration();
        _classifier = classifier;
        _knowledgeRegistry = knowledgeRegistry;
        _planner = planner;
    }

    public void AddKnowledgeProvider<TProvider, TResult>(TProvider provider)
        where TProvider : class, IKnowledgeProvider<TResult>
        where TResult : class, new()
    {
        if (_knowledgeRegistry is null)
            throw new InvalidOperationException("Knowledge system is not configured. Pass a KnowledgeProviderRegistry to the constructor.");
        _knowledgeRegistry.AddProvider<TProvider, TResult>(provider);
    }

    // ── Public API ────────────────────────────────────────────────

    public async Task<string?> SendMessageAsync(string message, string? personality = null)
    {
        Log(LogVerbosity.Normal, $"Sending message: {message}");

        if (_config.EnableClassification && _classifier is not null && _knowledgeRegistry is not null)
            await InjectKnowledgeAsync(message);

        if (_config.EnablePlanning && _planner is not null)
        {
            var response = await SendWithPlanningAsync(message);
            if (response is not null && !string.IsNullOrWhiteSpace(personality))
                response = await ApplyPersonalityAsync(response, personality);
            return response;
        }

        _conversation.Add(new Message { Role = "user", Content = message });
        TrimConversation();

        var result = await ProcessConversationAsync();

        if (result is not null && !string.IsNullOrWhiteSpace(personality))
            result = await ApplyPersonalityAsync(result, personality);

        return result;
    }

    public async IAsyncEnumerable<StreamChunk> SendMessageStreamingAsync(
        string message,
        string? personality = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        Log(LogVerbosity.Normal, $"Sending message (streaming): {message}");

        if (_config.EnableClassification && _classifier is not null && _knowledgeRegistry is not null)
            await InjectKnowledgeAsync(message);

        _conversation.Add(new Message { Role = "user", Content = message });
        TrimConversation();

        var maxIterations = 20;

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            var request = new LLMRequest
            {
                Messages = [.._conversation],
                Tools = _toolRegistry.GetAll().Count > 0 ? [.._toolRegistry.GetAll()] : null
            };

            Log(LogVerbosity.Verbose, $"Streaming request: {request.Messages.Count} messages, {request.Tools?.Count ?? 0} tools");

            List<ToolCall>? pendingToolCalls = null;
            var accumulatedContent = new System.Text.StringBuilder();

            await foreach (var chunk in _provider.SendStreamingAsync(request, ct))
            {
                if (chunk.Content is { Length: > 0 })
                {
                    accumulatedContent.Append(chunk.Content);
                    yield return new StreamChunk { Content = chunk.Content };
                }

                if (chunk.ToolCalls is { Count: > 0 })
                {
                    pendingToolCalls = chunk.ToolCalls;
                }

                if (chunk.IsComplete)
                {
                    if (pendingToolCalls is { Count: > 0 })
                    {
                        var content = accumulatedContent.Length > 0 ? accumulatedContent.ToString() : null;
                        _conversation.Add(new Message
                        {
                            Role = "assistant",
                            Content = content,
                            ToolCalls = pendingToolCalls
                        });
                    }
                    else
                    {
                        var content = accumulatedContent.Length > 0 ? accumulatedContent.ToString() : null;
                        if (content is not null)
                        {
                            _conversation.Add(new Message
                            {
                                Role = "assistant",
                                Content = content
                            });
                        }
                    }

                    Log(LogVerbosity.Verbose, $"Streaming chunk complete: content={accumulatedContent.Length} chars, toolCalls={pendingToolCalls?.Count ?? 0}");
                    break;
                }
            }

            if (pendingToolCalls is { Count: > 0 })
            {
                Log(LogVerbosity.Normal, $"Streaming paused for {pendingToolCalls.Count} tool call(s)");
                StreamingResponseReceived?.Invoke(this, new StreamingResponseReceivedEventArgs(
                    accumulatedContent.ToString(),
                    _conversation.AsReadOnly(),
                    isToolCallPause: true,
                    isComplete: false));

                foreach (var toolCall in pendingToolCalls)
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

            var responseText = accumulatedContent.ToString();
            if (!string.IsNullOrWhiteSpace(responseText) && !string.IsNullOrWhiteSpace(personality))
            {
                responseText = await ApplyPersonalityAsync(responseText, personality);
            }

            Log(LogVerbosity.Normal, $"Streaming response complete: {responseText}");
            StreamingResponseReceived?.Invoke(this, new StreamingResponseReceivedEventArgs(
                responseText,
                _conversation.AsReadOnly(),
                isToolCallPause: false,
                isComplete: true));
            ResponseReceived?.Invoke(this, new ResponseReceivedEventArgs(responseText, _conversation.AsReadOnly()));

            yield break;
        }

        Log(LogVerbosity.Minimal, "Max conversation iterations reached (streaming)");
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

    // ── Planning Path ─────────────────────────────────────────────

    private async Task<string?> SendWithPlanningAsync(string message)
    {
        try
        {
            var classification = await _classifier!.ClassifyAsync(message, _conversation.AsReadOnly());
            Log(LogVerbosity.Verbose, $"Classification for planning: {JsonSerializer.Serialize(classification)}");

            var procedures = ExtractProcedures();
            var tools = _toolRegistry.GetAll();

            Log(LogVerbosity.Verbose, $"Creating plan with {procedures.Count} procedures and {tools.Count} tools");

            var plan = await _planner!.CreatePlanAsync(message, classification, procedures, tools);

            Log(LogVerbosity.Verbose, $"Plan created: {JsonSerializer.Serialize(plan)}");

            _conversation.Add(new Message { Role = "system", Content = "Execution plan:" });
            var planDescription = FormatPlan(plan);
            _conversation.Add(new Message { Role = "system", Content = planDescription });

            var executionResults = await ExecutePlanAsync(plan);

            _conversation.Add(new Message { Role = "system", Content = "Plan execution results:" });
            foreach (var execResult in executionResults)
            {
                _conversation.Add(new Message
                {
                    Role = "system",
                    Content = execResult
                });
            }

            _conversation.Add(new Message { Role = "user", Content = message });
            TrimConversation();

            return await GetFinalResponseAsync();
        }
        catch (Exception ex)
        {
            Log(LogVerbosity.Minimal, $"Planning failed: {ex.Message}");

            _conversation.Add(new Message { Role = "user", Content = message });
            TrimConversation();

            return await ProcessConversationAsync();
        }
    }

    private List<Procedure> ExtractProcedures()
    {
        var procedures = new List<Procedure>();
        if (_knowledgeRegistry is null)
            return procedures;

        var allKnowledge = _conversation
            .Where(m => m.Role == "system" && m.Content is not null)
            .SelectMany(m => ExtractProceduresFromText(m.Content!))
            .ToList();

        return allKnowledge;
    }

    private static List<Procedure> ExtractProceduresFromText(string text)
    {
        var procedures = new List<Procedure>();

        try
        {
            var parsed = JsonSerializer.Deserialize<List<Procedure>>(text, JsonOpts);
            if (parsed is { Count: > 0 })
                return parsed;
        }
        catch
        {
        }

        try
        {
            var single = JsonSerializer.Deserialize<Procedure>(text, JsonOpts);
            if (single is not null)
                return [single];
        }
        catch
        {
        }

        return procedures;
    }

    private static string FormatPlan(Plan plan)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Goal: {plan.Goal}");
        sb.AppendLine();

        foreach (var step in plan.Steps)
        {
            sb.AppendLine($"{step.Order}. [{step.Action}] {step.Description}");
            if (step.ToolIdentifier is not null)
            {
                var args = step.Arguments is { Count: > 0 }
                    ? string.Join(", ", step.Arguments.Select(kv => $"{kv.Key}={kv.Value}"))
                    : "no args";
                sb.AppendLine($"   Tool: {step.ToolIdentifier}({args})");
            }
        }

        return sb.ToString();
    }

    private async Task<List<string>> ExecutePlanAsync(Plan plan)
    {
        var results = new List<string>();

        foreach (var step in plan.Steps.Where(s => s.Action == "tool-call"))
        {
            var definition = _toolRegistry.GetDefinition(step.ToolIdentifier ?? string.Empty);
            if (definition is null)
            {
                results.Add($"Step {step.Order}: Unknown tool '{step.ToolIdentifier}', skipping");
                continue;
            }

            Log(LogVerbosity.Normal, $"Plan step {step.Order}: executing {step.ToolIdentifier}");

            var positionalArgs = definition.BuildPositionalArgs(step.Arguments ?? []);

            if ((definition.Policy & ToolPolicy.RequiresPermission) != 0)
            {
                var pending = new PendingToolCall(step.ToolIdentifier!, positionalArgs);
                _pendingCalls[pending.Id] = pending;

                Log(LogVerbosity.Normal, $"Plan tool call pending: {pending.Id} ({step.ToolIdentifier})");
                ToolCallPending?.Invoke(this, new ToolCallPendingEventArgs(pending));

                var approval = await pending.WaitForApprovalAsync();
                _pendingCalls.TryRemove(pending.Id, out _);

                if (approval == ApprovalResult.Rejected)
                {
                    var reason = pending.RejectionReason ?? "No reason provided";
                    Log(LogVerbosity.Normal, $"Plan tool rejected: {pending.Id} ({step.ToolIdentifier}) - {reason}");
                    results.Add($"Step {step.Order}: {step.ToolIdentifier} rejected ({reason})");
                    continue;
                }

                Log(LogVerbosity.Normal, $"Plan tool approved: {pending.Id} ({step.ToolIdentifier})");
            }

            try
            {
                var result = await definition.ExecuteAsync(positionalArgs);
                var resultStr = definition.SerializeResult(result);
                Log(LogVerbosity.Normal, $"Plan step {step.Order} result: {resultStr}");
                results.Add($"Step {step.Order} ({step.ToolIdentifier}): {resultStr}");
            }
            catch (Exception ex)
            {
                Log(LogVerbosity.Minimal, $"Plan step {step.Order} failed: {ex.Message}");
                results.Add($"Step {step.Order} ({step.ToolIdentifier}): Error - {ex.Message}");
            }
        }

        return results;
    }

    private async Task<string?> GetFinalResponseAsync()
    {
        try
        {
            var request = new LLMRequest
            {
                Messages = [.._conversation],
                Tools = null
            };

            var response = await _provider.SendAsync(request);
            var text = response.Message?.Content;

            if (string.IsNullOrWhiteSpace(text))
            {
                Log(LogVerbosity.Verbose, "Final response was empty, retrying with fallback prompt");

                var fallbackRequest = new LLMRequest
                {
                    Messages =
                    [
                        .._conversation,
                        new Message
                        {
                            Role = "user",
                            Content = "Summarize the execution results above. If tools were called, report what was found. If no tools were needed, answer the original question directly."
                        }
                    ],
                    Tools = null
                };

                response = await _provider.SendAsync(fallbackRequest);
                text = response.Message?.Content ?? "Execution completed. Review the results above for details.";
            }

            Log(LogVerbosity.Normal, $"Final response: {text}");
            ResponseReceived?.Invoke(this, new ResponseReceivedEventArgs(text, _conversation.AsReadOnly()));
            return text;
        }
        catch (Exception ex)
        {
            Log(LogVerbosity.Minimal, $"Final response failed: {ex.Message}");
            return "I encountered an error while processing the results.";
        }
    }

    // ── Knowledge Injection ───────────────────────────────────────

    private async Task InjectKnowledgeAsync(string message)
    {
        try
        {
            Log(LogVerbosity.Verbose, "--- Knowledge Pipeline Start ---");

            var registeredProviders = _knowledgeRegistry!.GetProviders();
            Log(LogVerbosity.Verbose, $"Registered providers ({registeredProviders.Count}):");
            foreach (var p in registeredProviders)
                Log(LogVerbosity.Verbose, $"  - {p.Name}: Tags=[{string.Join(", ", p.Tags)}] Caps=[{string.Join(", ", p.Capabilities)}] Cost={p.Cost}");

            Log(LogVerbosity.Verbose, $"Classifying message with {Math.Min(_conversation.Count, 6)} recent conversation messages as context");
            var classification = await _classifier!.ClassifyAsync(message, _conversation.AsReadOnly());
            Log(LogVerbosity.Verbose, $"Classification result: {JsonSerializer.Serialize(classification)}");

            if (!classification.KnowledgeRequired)
            {
                Log(LogVerbosity.Verbose, "Knowledge not required, skipping provider queries");
                return;
            }

            var tags = classification.RelevantTags is { Length: > 0 } ? classification.RelevantTags : null;
            Log(LogVerbosity.Verbose, $"Querying providers with tags: {(tags is not null ? string.Join(", ", tags) : "(none)")}");

            var query = new KnowledgeQuery
            {
                Query = message,
                Tags = tags,
                MaxCost = ProviderCost.Medium
            };

            var results = await _knowledgeRegistry!.QueryAsync(query);
            Log(LogVerbosity.Verbose, $"Provider results: {results.Count} total");

            if (results.Count == 0)
            {
                Log(LogVerbosity.Verbose, "No knowledge results returned from any provider");
                return;
            }

            foreach (var result in results)
            {
                Log(LogVerbosity.Verbose, $"  Result from '{result.Source}': Kind={result.Kind}, ContentLength={result.Content.Length}, Tags=[{(result.Tags is not null ? string.Join(", ", result.Tags) : "")}]");
            }

            var maxResults = Math.Min(results.Count, _config.MaxKnowledgeResults);
            var truncated = results.Take(maxResults).ToList();

            var knowledgeText = _formatter.FormatAll(truncated);
            Log(LogVerbosity.Verbose, $"Injecting knowledge context ({knowledgeText.Length} chars):");
            Log(LogVerbosity.Verbose, knowledgeText);

            _conversation.Add(new Message { Role = "system", Content = knowledgeText });
            Log(LogVerbosity.Verbose, "--- Knowledge Pipeline End ---");
        }
        catch (Exception ex)
        {
            Log(LogVerbosity.Minimal, $"Knowledge injection failed: {ex.Message}");
        }
    }

    // ── Standard Conversation Loop ────────────────────────────────

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

            Log(LogVerbosity.Verbose, $"Raw LLM response: {JsonSerializer.Serialize(response)}");

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

    // ── Helpers ───────────────────────────────────────────────────

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
