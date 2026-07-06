namespace Relay.Tools;

public enum ApprovalResult
{
    Approved,
    Rejected
}

public class PendingToolCall
{
    private readonly TaskCompletionSource<ApprovalResult> _tcs = new();

    public Guid Id { get; }
    public string ToolIdentifier { get; }
    public object?[] Arguments { get; }
    public string? RejectionReason { get; private set; }
    public DateTime CreatedAt { get; }
    public bool IsCompleted { get; private set; }

    public PendingToolCall(string toolIdentifier, object?[] arguments)
    {
        Id = Guid.NewGuid();
        ToolIdentifier = toolIdentifier;
        Arguments = arguments;
        CreatedAt = DateTime.UtcNow;
    }

    public void Approve()
    {
        if (!IsCompleted)
        {
            IsCompleted = true;
            _tcs.TrySetResult(ApprovalResult.Approved);
        }
    }

    public void Reject(string? reason = null)
    {
        if (!IsCompleted)
        {
            RejectionReason = reason;
            IsCompleted = true;
            _tcs.TrySetResult(ApprovalResult.Rejected);
        }
    }

    internal Task<ApprovalResult> WaitForApprovalAsync() => _tcs.Task;
}
