namespace CodingAgent.Domain;

public sealed class ExecutionAttempt
{
    public int AttemptNumber { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public ExecutionResult ExecutionResult { get; init; } = default!;
    public ReviewResult? Review { get; set; }
    public string? FixSummary { get; set; }
}
