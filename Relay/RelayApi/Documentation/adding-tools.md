# Adding Tools

Tools allow the LLM to perform actions like querying databases, fetching data, or calling APIs. Tools are registered with `ToolRegistry` and executed after approval.

## Basic Tool Registration

```csharp
var registry = new ToolRegistry();

registry.AddTool(
    "weather.get_current",          // Unique identifier (namespaced)
    typeof(string),                 // Return type
    async (string location) =>      // Handler delegate
    {
        return $"22°C in {location}";
    },
    new ToolArgument(typeof(string), "location")  // Parameters
);
```

The tool identifier uses dot notation (e.g., `db.create_record`, `http.fetch`) to organize tools by domain.

## Parameters

Each `ToolArgument` defines a parameter the LLM must provide:

```csharp
new ToolArgument(typeof(string), "name")          // Required string
new ToolArgument(typeof(int), "count")            // Required integer
new ToolArgument(typeof(string), "unit", "celsius") // Optional with default
```

When the LLM omits an optional parameter, the default value is silently substituted.

## Multiple Parameters

```csharp
registry.AddTool(
    "math.calculate",
    typeof(double),
    async (double a, double b, string operation) =>
    {
        return operation switch
        {
            "add" => a + b,
            "subtract" => a - b,
            "multiply" => a * b,
            "divide" => a / b,
            _ => throw new ArgumentException($"Unknown operation: {operation}")
        };
    },
    new ToolArgument(typeof(double), "a"),
    new ToolArgument(typeof(double), "b"),
    new ToolArgument(typeof(string), "operation")
);
```

## Async Handlers

All tool handlers must be async (return `Task<T>`):

```csharp
registry.AddTool(
    "db.query",
    typeof(string),
    async (string sql) =>
    {
        await using var conn = new SqlConnection(connectionString);
        var result = await conn.QueryAsync(sql);
        return JsonSerializer.Serialize(result);
    },
    new ToolArgument(typeof(string), "sql")
);
```

## Error Handling

If a tool handler throws an exception, the error message is sent back to the LLM and logged:

```csharp
registry.AddTool(
    "file.read",
    typeof(string),
    async (string path) =>
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}");
        return await File.ReadAllTextAsync(path);
    },
    new ToolArgument(typeof(string), "path")
);
```

## Tool Visibility

Tools are sent to the LLM on every request. If you need to conditionally enable tools, manage separate `ToolRegistry` instances.

## Best Practices

- **Use descriptive identifiers** — `db.create_record` is clearer than `create`
- **Keep handlers focused** — each tool should do one thing
- **Validate inputs** — check argument values before acting
- **Return meaningful strings** — the LLM reads the result to form its response
- **Handle errors gracefully** — throw descriptive exceptions so the LLM understands what went wrong
