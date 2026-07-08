using System.Text.RegularExpressions;
using Relay.Knowledge;

namespace Relay.TestApplication.Knowledge;

public class MarkdownSkillProvider : IKnowledgeProvider<List<Procedure>>
{
    private readonly string _skillsDirectory;
    private List<Procedure>? _cachedSkills;

    public MarkdownSkillProvider(string skillsDirectory)
    {
        _skillsDirectory = skillsDirectory;
    }

    public string Name => "Markdown Skills";

    public string Description => "Provides procedural knowledge from hierarchical markdown skill files";

    public string[] Tags => ["skills", "procedures", "documentation", "reference", "how-to", "filesystem", "navigation", "search", "csharp", "php", "development", "debugging", "file"];

    public string[] Capabilities => ["procedures", "documentation", "reference"];

    public ProviderCost Cost => ProviderCost.Low;

    public Task<List<Procedure>> FetchAsync(string query, CancellationToken ct = default)
    {
        var skills = LoadSkills();
        var terms = Tokenize(query);

        var result = new List<Procedure>();

        foreach (var skill in skills)
        {
            if (ct.IsCancellationRequested) break;

            var score = ScoreRelevance(skill, terms);
            if (score > 0 && !result.Contains(skill))
            {
                result.Add(skill);
            }
        }

        return Task.FromResult(result);
    }

    private static readonly HashSet<string> Stopwords =
    [
        "the", "this", "that", "these", "those", "a", "an",
        "is", "are", "was", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "will", "would", "can", "could",
        "shall", "should", "may", "might",
        "in", "on", "at", "to", "for", "of", "by", "with", "from",
        "and", "or", "but", "not", "so", "if", "as",
        "about", "into", "over", "after", "before", "between", "under",
        "i", "me", "my", "we", "our", "you", "your", "he", "she", "it", "they", "them",
        "what", "who", "where", "when", "why", "how",
        "all", "every", "each", "some", "any", "no", "none",
        "which", "there", "here", "please"
    ];

    private static string[] Tokenize(string query)
    {
        return query.Split([' ', ',', '.', '?', '!'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => new string(t.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray()))
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length > 2 && !Stopwords.Contains(t))
            .Distinct()
            .ToArray();
    }

    private static int ScoreRelevance(Procedure skill, string[] terms)
    {
        var score = 0;

        foreach (var term in terms)
        {
            if (skill.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase)))
                score += 3;
            else if (skill.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 2;
            else if ((skill.Category ?? "").Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 1;
            else if ((skill.Subcategory ?? "").Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 1;
            else if (skill.Steps.Any(p => p.Contains(term, StringComparison.OrdinalIgnoreCase)))
                score += 1;
        }

        return score;
    }

    private List<Procedure> LoadSkills()
    {
        if (_cachedSkills is not null)
            return _cachedSkills;

        _cachedSkills = [];

        if (!Directory.Exists(_skillsDirectory))
            return _cachedSkills;

        var files = Directory.GetFiles(_skillsDirectory, "*.md", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            try
            {
                var skill = ParseSkillFile(file);
                if (skill is not null)
                    _cachedSkills.Add(skill);
            }
            catch
            {
            }
        }

        return _cachedSkills;
    }

    private static Procedure? ParseSkillFile(string path)
    {
        var content = File.ReadAllText(path);
        var title = string.Empty;
        var tags = new List<string>();
        var tools = new List<string>();

        if (content.StartsWith("---"))
        {
            var end = content.IndexOf("---", 3, StringComparison.Ordinal);
            if (end > 3)
            {
                var frontmatter = content[3..end].Trim();
                content = content[(end + 3)..].Trim();

                foreach (var line in frontmatter.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var colonIdx = line.IndexOf(':');
                    if (colonIdx < 0) continue;

                    var key = line[..colonIdx].Trim().ToLowerInvariant();
                    var value = line[(colonIdx + 1)..].Trim();

                    switch (key)
                    {
                        case "title":
                            title = value;
                            break;
                        case "tags":
                            tags = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(t => t.Trim().ToLowerInvariant())
                                .ToList();
                            break;
                        case "tools":
                            tools = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(t => t.Trim())
                                .ToList();
                            break;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(title))
            title = Path.GetFileNameWithoutExtension(path);

        var skillsRoot = FindSkillsRoot(path);
        var relDir = Path.GetRelativePath(skillsRoot, Path.GetDirectoryName(path)!);
        var parts = relDir.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var category = parts.Length > 0 ? parts[0] : string.Empty;
        var subcategory = parts.Length > 1 ? string.Join("/", parts.Skip(1)) : string.Empty;

        var steps = new List<string>();
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            var step = Regex.Replace(trimmed, @"^\d+[\.\)]\s*", "");
            steps.Add(step);
        }

        return new Procedure
        {
            Name = title,
            Tags = [.. tags],
            RequiredTools = [.. tools],
            Steps = [.. steps],
            Category = category,
            Subcategory = subcategory
        };
    }

    private static string FindSkillsRoot(string filePath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(filePath)!);
        while (dir is not null)
        {
            if (dir.Name.Equals("skills", StringComparison.OrdinalIgnoreCase))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Path.GetDirectoryName(filePath)!;
    }
}
