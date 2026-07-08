using Relay.Abstractions;

namespace Relay.Classification;

public interface IKnowledgeClassifier
{
    Task<ClassificationResult> ClassifyAsync(string message, IReadOnlyList<Message>? conversation = null, CancellationToken ct = default);
}
