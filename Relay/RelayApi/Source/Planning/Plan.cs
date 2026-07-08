namespace Relay.Planning;

public class Plan
{
    public string Goal { get; init; } = string.Empty;

    public List<PlanStep> Steps { get; init; } = [];
}
