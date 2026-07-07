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
    "Get the current weather",      // Description (optional — helps the LLM understand the tool)
    null,                           // Usage instructions (optional — guides the LLM on invocation)
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

## Description & Usage

Each tool can include a **description** and **usage instructions** that are sent to the LLM:

- **Description** — a short summary of what the tool does (shown to the LLM when deciding which tool to call)
- **Usage** — guidance on how to invoke the tool correctly (appended to the description with a "Usage:" prefix)

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
    "Perform mathematical calculations",
    "Use 'add' for addition, 'subtract' for subtraction, 'multiply' for multiplication, 'divide' for division, and 'square root' or 'sqrt' for square root. Square root only uses parameter 'a'.",
    new ToolArgument(typeof(double), "a"),
    new ToolArgument(typeof(double), "b"),
    new ToolArgument(typeof(string), "operation")
);
```

When sent to the LLM, the description field combines both values:

> Perform mathematical calculations
>
> Usage: Use 'add' for addition, 'subtract' for subtraction, 'multiply' for multiplication, 'divide' for division, and 'square root' or 'sqrt' for square root. Square root only uses parameter 'a'.

This is especially useful for tools with non-obvious invocation patterns (e.g., a calculator that supports square root via a string parameter).

## Multiple Parameters

## Permission Policies

Tools can be registered with a `ToolPolicy` to control whether they require approval before execution. The policy is a bit-flag enum, making it extensible for future flags.

```csharp
// Requires user approval before execution
registry.AddTool(
    "db.create",
    typeof(string),
    async (string record) => $"Created: {record}",
    ToolPolicy.RequiresPermission,
    "Create a database record",
    null,
    new ToolArgument(typeof(string), "record")
);

// No permission needed — executes instantly (default)
registry.AddTool(
    "math.calculate",
    typeof(double),
    async (double a, double b, string op) => op switch
    {
        "add" => a + b,
        _ => throw new ArgumentException()
    },
    "Perform calculations",
    null,
    new ToolArgument(typeof(double), "a"),
    new ToolArgument(typeof(double), "b"),
    new ToolArgument(typeof(string), "operation")
);
```

### Overriding Policies After Registration

When importing third-party tools that default to no permission, you can lock them down with `ChangePolicy`:

```csharp
registry.ChangePolicy("db.create", ToolPolicy.RequiresPermission);
```

This modifies the tool's policy in place. If the identifier doesn't exist, a `KeyNotFoundException` is thrown.

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
