using Relay.Abstractions;
using Relay.Configuration;
using Relay.Events;
using Relay.Logging;
using Relay.Providers;
using Relay.Services;
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
            MaxConversationHistory = 100
        };

        // ── Tool registration ────────────────────────────────────
        var registry = new ToolRegistry();

        // Requires explicit approval
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

        // No approval needed (ToolPolicy.None is the default)
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

        // Override policy after registration (e.g., lock down a third-party tool)
        registry.ChangePolicy("math.calculate", ToolPolicy.RequiresPermission);

        // ── Provider & Service ───────────────────────────────────
        var provider = new OllamaProvider(config.OllamaBaseUrl, config.OllamaModel);
        var relay = new RelayService(provider, registry, config)
        {
            Verbosity = LogVerbosity.Normal,
            Logger = logger
        };

        // ── Event listeners ──────────────────────────────────────
        relay.ToolCallPending += OnToolCallPending;
        relay.ResponseReceived += OnResponseReceived;

        // ── Chat loop ────────────────────────────────────────────
        Console.WriteLine("Relay chat (type 'exit' to quit, 'clear' to reset)");
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

    private static void OnToolCallPending(object? sender, ToolCallPendingEventArgs e)
    {
        var call = e.PendingCall;
        Console.WriteLine($"[Pending tool: {call.ToolIdentifier} (id: {call.Id})]");

        // Auto-approve in this demo
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
