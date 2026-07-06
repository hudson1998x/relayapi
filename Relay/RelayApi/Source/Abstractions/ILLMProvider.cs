namespace Relay.Abstractions;

public interface ILLMProvider
{
    Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken ct = default);
}
