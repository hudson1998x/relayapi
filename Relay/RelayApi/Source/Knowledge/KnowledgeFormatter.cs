using System.Text.Json;

namespace Relay.Knowledge;

public class KnowledgeFormatter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Format(KnowledgeResult result)
    {
        return result.Kind switch
        {
            "Procedure" => FormatProcedure(result.Content),
            _ => FormatData(result)
        };
    }

    public string FormatAll(IReadOnlyList<KnowledgeResult> results)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var result in results)
        {
            if (IsEmpty(result))
                continue;

            if (sb.Length > 0)
                sb.AppendLine();

            sb.AppendLine(Format(result));
        }

        return sb.ToString();
    }

    private static bool IsEmpty(KnowledgeResult result)
    {
        var trimmed = result.Content.Trim();
        return trimmed is "" or "[]" or "{}" or "\"\"";
    }

    private static string FormatProcedure(string content)
    {
        try
        {
            var procedures = JsonSerializer.Deserialize<List<Procedure>>(content, JsonOpts);
            if (procedures is { Count: > 0 })
                return string.Join("\n\n", procedures.Select(FormatSingleProcedure));

            var single = JsonSerializer.Deserialize<Procedure>(content, JsonOpts);
            if (single is not null)
                return FormatSingleProcedure(single);
        }
        catch
        {
        }

        return content;
    }

    private static string FormatSingleProcedure(Procedure proc)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine(proc.Name);

        if (proc.RequiredTools is { Length: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Required tools:");
            foreach (var tool in proc.RequiredTools)
                sb.AppendLine($"  \u2022 {tool}");
        }

        if (proc.Steps is { Length: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Procedure:");
            for (int i = 0; i < proc.Steps.Length; i++)
                sb.AppendLine($"  {i + 1}. {proc.Steps[i]}");
        }

        if (proc.Examples is { Length: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Examples:");
            foreach (var example in proc.Examples)
                sb.AppendLine($"  \u2022 {example}");
        }

        return sb.ToString();
    }

    private static string FormatData(KnowledgeResult result)
    {
        if (result.Source is not null || result.Tags is { Length: > 0 })
        {
            var sb = new System.Text.StringBuilder();
            if (result.Source is not null)
                sb.AppendLine($"From: {result.Source}");
            if (result.Tags is { Length: > 0 })
                sb.AppendLine($"Tags: {string.Join(", ", result.Tags)}");
            sb.AppendLine();
            sb.Append(result.Content);
            return sb.ToString();
        }

        return result.Content;
    }
}
