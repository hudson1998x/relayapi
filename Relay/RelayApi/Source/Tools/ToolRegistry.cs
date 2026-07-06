namespace Relay.Tools;

public class ToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void AddTool(
        string identifier,
        Type returnType,
        Delegate handler,
        params ToolArgument[] arguments)
    {
        AddTool(identifier, returnType, handler, false, arguments);
    }

    public void AddTool(
        string identifier,
        Type returnType,
        Delegate handler,
        bool requiresPermission,
        params ToolArgument[] arguments)
    {
        var definition = new ToolDefinition(identifier, arguments, returnType, handler, requiresPermission);
        _tools[identifier] = definition;
    }

    public ToolDefinition? GetDefinition(string identifier)
    {
        _tools.TryGetValue(identifier, out var definition);
        return definition;
    }

    public IReadOnlyList<ToolDefinition> GetAll() => _tools.Values.ToList();
}
