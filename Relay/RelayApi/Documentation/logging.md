# Logging

Relay has a built-in logging system with configurable verbosity.

## Verbosity Levels

| Level | Description |
|---|---|
| `None` | No logging |
| `Minimal` | Errors, tool rejections, execution failures |
| `Normal` | Messages sent, responses received, tool calls (default) |
| `Verbose` | Full conversation dumps, raw LLM JSON responses |

## Setting Up a Logger

Implement `IRelayLogger`:

```csharp
public class ConsoleLogger : IRelayLogger
{
    public void Log(LogVerbosity level, string message)
    {
        var prefix = level switch
        {
            LogVerbosity.Minimal => "[ERR]",
            LogVerbosity.Normal  => "[INF]",
            LogVerbosity.Verbose => "[DBG]",
            _ => "[?]"
        };
        Console.WriteLine($"{prefix} {message}");
    }
}
```

Assign it to the service:

```csharp
var relay = new RelayService(provider, registry)
{
    Logger = new ConsoleLogger(),
    Verbosity = LogVerbosity.Normal
};
```

## What Gets Logged

### Minimal
- LLM request failures
- Tool execution exceptions
- Unknown tool identifiers
- Max conversation iterations reached
- Tool rejections with reason

### Normal (includes Minimal)
- User message content
- Tool call pending (id + identifier)
- Tool approved/rejected
- Final response text
- Conversation cleared

### Verbose (includes Normal)
- Raw LLM response JSON
- Tool execution results

## Custom Loggers

Use this to integrate with any logging framework:

```csharp
public class SerilogLogger : IRelayLogger
{
    private readonly Serilog.ILogger _logger;

    public SerilogLogger(Serilog.ILogger logger) => _logger = logger;

    public void Log(LogVerbosity level, string message)
    {
        var serilogLevel = level switch
        {
            LogVerbosity.Minimal => Serilog.Events.LogEventLevel.Error,
            LogVerbosity.Normal  => Serilog.Events.LogEventLevel.Information,
            LogVerbosity.Verbose => Serilog.Events.LogEventLevel.Debug,
            _ => Serilog.Events.LogEventLevel.Information
        };
        _logger.Write(serilogLevel, "{RelayMessage}", message);
    }
}
```
