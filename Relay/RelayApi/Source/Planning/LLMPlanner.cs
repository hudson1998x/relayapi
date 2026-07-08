using System.Text.Json;
using Relay.Abstractions;
using Relay.Classification;
using Relay.Knowledge;
using Relay.Tools;

namespace Relay.Planning;

public class LLMPlanner : IPlanner
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILLMProvider _provider;
    private readonly string _systemPrompt;

    public LLMPlanner(ILLMProvider provider, string? systemPrompt = null)
    {
        _provider = provider;
        _systemPrompt = systemPrompt ?? DefaultSystemPrompt;
    }

    public static string DefaultSystemPrompt { get; } = """
        You are a planner. Your job is to create an efficient execution plan for the user's request.

        You will receive:
        - The user's message
        - A classification (category, tags)
        - Available procedures (how-to guides)
        - Available tools (capabilities)

        Produce a JSON plan with:
        - goal: a short description of what needs to be done
        - steps: ordered list of actions

        Each step has:
        - order: step number starting at 1
        - action: "tool-call" to invoke a tool, "verify" to check results, "report" to produce output
        - toolIdentifier: the tool name (only for "tool-call" actions)
        - arguments: dictionary of argument names to values (only for "tool-call" actions)
        - description: what this step accomplishes

        Rules:
        - Only use tools listed in the available tools section
        - Follow procedures exactly when available
        - Break complex tasks into small, verifiable steps
        - Include verification steps after critical operations
        - End with a "report" step to produce the final output

        Example:
        User: Find the UserProvider class
        Classification: Development / C#, tags=[csharp, navigation]
        Procedure: Finding a C# Class
        Tools: directory.list(path), file.search(path, pattern)

        Plan:
        {
          "goal": "Locate the UserProvider class file",
          "steps": [
            {"order": 1, "action": "tool-call", "toolIdentifier": "directory.list", "arguments": {"path": "."}, "description": "List project root"},
            {"order": 2, "action": "tool-call", "toolIdentifier": "file.search", "arguments": {"path": ".", "pattern": "UserProvider"}, "description": "Search for UserProvider"},
            {"order": 3, "action": "report", "description": "Report the file location"}
          ]
        }
        """;

    public async Task<Plan> CreatePlanAsync(
        string userMessage,
        ClassificationResult classification,
        IReadOnlyList<Procedure> procedures,
        IReadOnlyList<ToolDefinition> availableTools,
        CancellationToken ct = default)
    {
        var userContent = BuildPlannerMessage(userMessage, classification, procedures, availableTools);

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
                return DefaultPlan(userMessage);

            var json = ExtractJson(content);
            if (json is null)
                return DefaultPlan(userMessage);

            var plan = JsonSerializer.Deserialize<Plan>(json, JsonOpts);
            return plan ?? DefaultPlan(userMessage);
        }
        catch
        {
            return DefaultPlan(userMessage);
        }
    }

    private static string BuildPlannerMessage(
        string message,
        ClassificationResult classification,
        IReadOnlyList<Procedure> procedures,
        IReadOnlyList<ToolDefinition> tools)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("User message:");
        sb.AppendLine(message);
        sb.AppendLine();

        sb.AppendLine("Classification:");
        sb.AppendLine($"  Category: {classification.Category}");
        if (classification.Subcategory is not null)
            sb.AppendLine($"  Subcategory: {classification.Subcategory}");
        if (classification.RelevantTags is { Length: > 0 })
            sb.AppendLine($"  Tags: {string.Join(", ", classification.RelevantTags)}");
        sb.AppendLine();

        if (procedures.Count > 0)
        {
            sb.AppendLine("Available procedures:");
            foreach (var proc in procedures)
            {
                var procJson = JsonSerializer.Serialize(proc, JsonOpts);
                sb.AppendLine($"  - {proc.Name}: {procJson}");
            }
            sb.AppendLine();
        }

        if (tools.Count > 0)
        {
            sb.AppendLine("Available tools:");
            foreach (var tool in tools)
            {
                var args = string.Join(", ", tool.Arguments.Select(a => $"{a.Name}({a.Type.Name})"));
                sb.AppendLine($"  - {tool.Identifier}({args})");
                if (!string.IsNullOrWhiteSpace(tool.Description))
                    sb.AppendLine($"    Description: {tool.Description}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Produce a JSON plan for execution.");

        return sb.ToString();
    }

    private static Plan DefaultPlan(string message)
    {
        return new Plan
        {
            Goal = $"Respond to: {message}",
            Steps =
            [
                new PlanStep
                {
                    Order = 1,
                    Action = "report",
                    Description = "Unable to create a detailed plan, produce a direct response"
                }
            ]
        };
    }

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
