using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Relay.Tools;

public class ToolDefinition
{
    private static readonly JsonSerializerOptions StrictJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Identifier { get; }
    public IReadOnlyList<ToolArgument> Arguments { get; }
    public Type ReturnType { get; }
    public Delegate Handler { get; }
    public bool RequiresPermission { get; }

    public ToolDefinition(string identifier, IReadOnlyList<ToolArgument> arguments, Type returnType, Delegate handler, bool requiresPermission = false)
    {
        Identifier = identifier;
        Arguments = arguments;
        ReturnType = returnType;
        Handler = handler;
        RequiresPermission = requiresPermission;
    }

    public async Task<object?> ExecuteAsync(object?[] positionalArgs)
    {
        var result = Handler.DynamicInvoke(positionalArgs);

        if (result is Task task)
        {
            await task;
            var resultProperty = task.GetType().GetProperty("Result");
            return resultProperty?.GetValue(task);
        }

        return result;
    }

    public string SerializeResult(object? result)
    {
        if (result is null)
            return "null";

        if (ReturnType == typeof(string) || ReturnType.IsPrimitive)
            return result.ToString() ?? "null";

        return JsonSerializer.Serialize(result, ReturnType, StrictJsonOpts);
    }

    internal object?[] BuildPositionalArgs(Dictionary<string, object?> namedArgs)
    {
        var args = new object?[Arguments.Count];
        for (int i = 0; i < Arguments.Count; i++)
        {
            var argDef = Arguments[i];
            if (namedArgs.TryGetValue(argDef.Name, out var value))
            {
                args[i] = ConvertValue(value, argDef.Type);
            }
            else
            {
                args[i] = argDef.DefaultValue;
            }
        }
        return args;
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value is null)
            return null;

        var type = value.GetType();

        if (targetType.IsInstanceOfType(value))
            return value;

        return JsonSerializer.Deserialize(
            JsonSerializer.Serialize(value), targetType);
    }
}
