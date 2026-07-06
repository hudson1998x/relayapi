namespace Relay.Tools;

public class ToolArgument
{
    public Type Type { get; }
    public string Name { get; }
    public object? DefaultValue { get; }

    public ToolArgument(Type type, string name, object? defaultValue = null)
    {
        Type = type;
        Name = name;
        DefaultValue = defaultValue;
    }
}
