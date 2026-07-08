using Relay.Abstractions;
using Relay.Classification;
using Relay.Knowledge;
using Relay.Tools;

namespace Relay.Planning;

public interface IPlanner
{
    Task<Plan> CreatePlanAsync(
        string userMessage,
        ClassificationResult classification,
        IReadOnlyList<Procedure> procedures,
        IReadOnlyList<ToolDefinition> availableTools,
        CancellationToken ct = default);
}
