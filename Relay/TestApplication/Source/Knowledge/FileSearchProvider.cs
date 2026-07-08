using System.Text.Json;
using Relay.Knowledge;

namespace Relay.TestApplication.Knowledge;

public class FileSearchResult
{
    public List<FileSearchHit> Hits { get; set; } = [];
    public int TotalMatches { get; set; }
    public string SearchTerms { get; set; } = string.Empty;
}

public class FileSearchHit
{
    public string Path { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
}

public class FileSearchProvider : IKnowledgeProvider<FileSearchResult>
{
    private readonly string _rootDirectory;
    private const int MaxResults = 10;

    public FileSearchProvider(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
    }

    public string Name => "File Search";

    public string Description => "Searches filenames in the project directory for quick file discovery";

    public string[] Tags => ["filesystem", "navigation", "search", "source-code", "file"];

    public string[] Capabilities => ["filename-search", "directory-navigation"];

    public ProviderCost Cost => ProviderCost.Low;

    public Task<FileSearchResult> FetchAsync(string query, CancellationToken ct = default)
    {
        var result = new FileSearchResult
        {
            SearchTerms = query
        };

        if (!Directory.Exists(_rootDirectory))
            return Task.FromResult(result);

        var terms = query.Split([' ', ',', '"', '\''], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => new string(t.Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '-').ToArray()))
            .Where(t => t.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (terms.Length == 0)
            return Task.FromResult(result);

        try
        {
            var allFiles = Directory.GetFiles(_rootDirectory, "*.*", SearchOption.AllDirectories);
            var allDirs = Directory.GetDirectories(_rootDirectory, "*", SearchOption.AllDirectories);

            foreach (var file in allFiles)
            {
                if (ct.IsCancellationRequested || result.Hits.Count >= MaxResults) break;

                if (terms.Any(t => Matches(file, t)))
                {
                    result.Hits.Add(new FileSearchHit
                    {
                        Path = file,
                        FileName = Path.GetFileName(file),
                        IsDirectory = false
                    });
                }
            }

            foreach (var dir in allDirs)
            {
                if (ct.IsCancellationRequested || result.Hits.Count >= MaxResults) break;

                var dirName = Path.GetFileName(dir);
                if (terms.Any(t => Matches(dirName, t)))
                {
                    result.Hits.Add(new FileSearchHit
                    {
                        Path = dir,
                        FileName = dirName,
                        IsDirectory = true
                    });
                }
            }

            result.TotalMatches = result.Hits.Count;
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Task.FromResult(result);
    }

    private static bool Matches(string path, string term)
    {
        var fullName = Path.GetFileName(path);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(path);

        if (string.IsNullOrEmpty(nameWithoutExt))
            return fullName.Equals(term, StringComparison.OrdinalIgnoreCase) ||
                   fullName.StartsWith(term + ".", StringComparison.OrdinalIgnoreCase) ||
                   term.Equals(fullName, StringComparison.OrdinalIgnoreCase);

        return fullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               nameWithoutExt.Equals(term, StringComparison.OrdinalIgnoreCase) ||
               term.Equals(nameWithoutExt, StringComparison.OrdinalIgnoreCase);
    }
}
