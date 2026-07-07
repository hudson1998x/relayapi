namespace Relay.Tools;

public class ToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void AddTool(
        string identifier,
        Type returnType,
        Delegate handler,
        string? description = null,
        string? usage = null,
        params ToolArgument[] arguments)
    {
        var definition = new ToolDefinition(identifier, arguments, returnType, handler, ToolPolicy.None, description, usage);
        _tools[identifier] = definition;
    }

    public void AddTool(
        string identifier,
        Type returnType,
        Delegate handler,
        ToolPolicy policy,
        string? description = null,
        string? usage = null,
        params ToolArgument[] arguments)
    {
        var definition = new ToolDefinition(identifier, arguments, returnType, handler, policy, description, usage);
        _tools[identifier] = definition;
    }

    public void ChangePolicy(string identifier, ToolPolicy policy)
    {
        if (!_tools.TryGetValue(identifier, out var definition))
            throw new KeyNotFoundException($"Tool '{identifier}' not found.");

        definition.Policy = policy;
    }

    public ToolDefinition? GetDefinition(string identifier)
    {
        _tools.TryGetValue(identifier, out var definition);
        return definition;
    }

    public IReadOnlyList<ToolDefinition> GetAll() => _tools.Values.ToList();
}
