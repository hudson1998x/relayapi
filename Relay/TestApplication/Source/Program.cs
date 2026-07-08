using Relay.Abstractions;
using Relay.Classification;
using Relay.Configuration;
using Relay.Events;
using Relay.Knowledge;
using Relay.Logging;
using Relay.Providers;
using Relay.Services;
using Relay.TestApplication.Knowledge;
using Relay.Tools;

namespace Relay.TestApplication;

class Program
{
    static async Task Main(string[] args)
    {
        // ── Logging ──────────────────────────────────────────────
        var logger = new ConsoleLogger();
        var config = new RelayConfiguration
        {
            OllamaModel = "qwen3",
            MaxConversationHistory = 100,
            EnableClassification = true,
            EnablePlanning = true
        };

        // ── Tool registration ────────────────────────────────────
        var registry = new ToolRegistry();

        registry.AddTool(
            "weather.get_current",
            typeof(WeatherResult),
            async (string location, string unit) =>
            {
                await Task.Delay(100);
                return new WeatherResult
                {
                    Location = location,
                    TemperatureCelsius = 22,
                    Conditions = "Clear skies",
                    Humidity = 45
                };
            },
            ToolPolicy.RequiresPermission,
            "Get the current weather for a location",
            null,
            new ToolArgument(typeof(string), "location"),
            new ToolArgument(typeof(string), "unit", "celsius")
        );

        registry.AddTool(
            "math.calculate",
            typeof(double),
            async (double a, double b, string operation) =>
            {
                await Task.Delay(50);
                return operation switch
                {
                    "add" => a + b,
                    "subtract" => a - b,
                    "multiply" => a * b,
                    "divide" => a / b,
                    "square root" or "sqrt" => Math.Sqrt(a),
                    _ => throw new ArgumentException($"Unknown operation: {operation}")
                };
            },
            "Perform mathematical calculations",
            "Use 'add' for addition, 'subtract' for subtraction, 'multiply' for multiplication, 'divide' for division, and 'square root' or 'sqrt' for square root. Square root only uses parameter 'a'.",
            new ToolArgument(typeof(double), "a"),
            new ToolArgument(typeof(double), "b"),
            new ToolArgument(typeof(string), "operation")
        );

        registry.AddTool(
            "directory.list",
            typeof(List<string>),
            async (string dir) =>
            {
                await Task.Delay(50);
                var files = new List<string>();

                files.AddRange(Directory.GetDirectories(dir));
                files.AddRange(Directory.GetFiles(dir));
                return files;
            },
            "List all files and directories in the given directory",
            "Use this to explore directory contents. Pass the full directory path.",
            new ToolArgument(typeof(string), "directory")
        );

        registry.AddTool(
            "file.read",
            typeof(string),
            async (string path, int startLine) =>
            {
                await Task.Delay(50);
                if (!File.Exists(path))
                    return $"File not found: {path}";

                var lines = await File.ReadAllLinesAsync(path);
                if (startLine < 1) startLine = 1;
                if (startLine > lines.Length)
                    return $"Line {startLine} exceeds file length ({lines.Length} lines).";

                var end = Math.Min(startLine + 19, lines.Length);
                var segment = new List<string>();
                for (int i = startLine - 1; i < end; i++)
                    segment.Add($"{i + 1}: {lines[i]}");

                var result = string.Join('\n', segment);
                var hasMore = end < lines.Length;
                result += $"\n\n[Lines {startLine}-{end} of {lines.Length}";
                result += hasMore ? $"; use startLine={end + 1} for next segment]" : "; end of file]";
                return result;
            },
            "Reads 20 lines from a file starting at the given line number",
            "Use this to inspect source files in segments. Call repeatedly with increasing startLine to scan through the full file.",
            new ToolArgument(typeof(string), "path"),
            new ToolArgument(typeof(int), "startLine", 1)
        );

        registry.ChangePolicy("math.calculate", ToolPolicy.RequiresPermission);

        // ── LLM Provider ──────────────────────────────────────────
        var llmProvider = new OllamaProvider(config.OllamaBaseUrl, config.OllamaModel);

        // ── Knowledge System ──────────────────────────────────────
        var classifier = new LLMClassifier(llmProvider);

        var knowledgeRegistry = new KnowledgeProviderRegistry();

        // Search project source files by filename
        var projectRoot = FindProjectRoot();
        knowledgeRegistry.AddProvider<FileSearchProvider, FileSearchResult>(
            new FileSearchProvider(projectRoot));

        // Load procedural skills from markdown files
        var skillsDir = Path.Combine(projectRoot, "Relay", "TestApplication", "skills");
        if (Directory.Exists(skillsDir))
        {
            knowledgeRegistry.AddProvider<MarkdownSkillProvider, List<Procedure>>(
                new MarkdownSkillProvider(skillsDir));
        }

        // ── Provider & Service ───────────────────────────────────
        var relay = new RelayService(llmProvider, registry, config, classifier, knowledgeRegistry)
        {
            Verbosity = LogVerbosity.Verbose,
            Logger = logger
        };

        // ── Event listeners ──────────────────────────────────────
        relay.ToolCallPending += OnToolCallPending;
        relay.ResponseReceived += OnResponseReceived;

        // ── Chat loop ────────────────────────────────────────────
        Console.WriteLine("Relay chat (type 'exit' to quit, 'clear' to reset)");
        Console.WriteLine("Knowledge system: enabled");
        Console.WriteLine($"Skills directory: {skillsDir}");
        Console.WriteLine(new string('-', 50));

        while (true)
        {
            Console.Write("You: ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;
            if (input == "exit") break;
            if (input == "clear")
            {
                relay.ClearConversation();
                continue;
            }

            string? personality = null;
            var message = input;

            if (input.StartsWith('@'))
            {
                var spaceIdx = input.IndexOf(' ');
                if (spaceIdx > 1)
                {
                    personality = input[1..spaceIdx].Trim();
                    message = input[(spaceIdx + 1)..].Trim();
                    Console.WriteLine($"[Personality: {personality}]");
                }
            }

            var response = await relay.SendMessageAsync(message, personality);
            Console.WriteLine($"Relay: {response}");
            Console.WriteLine();
        }
    }

    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Relay.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        return AppContext.BaseDirectory;
    }

    private static void OnToolCallPending(object? sender, ToolCallPendingEventArgs e)
    {
        var call = e.PendingCall;
        Console.WriteLine($"[Pending tool: {call.ToolIdentifier} (id: {call.Id})]");

        call.Approve();
    }

    private static void OnResponseReceived(object? sender, ResponseReceivedEventArgs e)
    {
        Console.WriteLine(new string('-', 50));
    }
}

public class WeatherResult
{
    public string Location { get; set; } = string.Empty;
    public double TemperatureCelsius { get; set; }
    public string Conditions { get; set; } = string.Empty;
    public int Humidity { get; set; }
}

public class ConsoleLogger : IRelayLogger
{
    public void Log(LogVerbosity level, string message)
    {
        var prefix = level switch
        {
            LogVerbosity.Minimal => "[ERR]",
            LogVerbosity.Normal => "[INF]",
            LogVerbosity.Verbose => "[DBG]",
            _ => "[?]"
        };
        Console.WriteLine($"{prefix} {message}");
    }
}
