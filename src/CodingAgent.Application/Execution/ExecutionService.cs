using CodingAgent.Application.Abstractions;
using CodingAgent.Domain;

namespace CodingAgent.Application.Execution;

public sealed class ExecutionService(
    IAgentRunRepository repository)
{
    public async Task<ExecutionResponse> ExecuteAsync(
        Guid executionId,
        ExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var run = await repository.GetAsync(executionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Run '{executionId}' was not found.");

        if (request.TimeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "TimeoutSeconds must be greater than zero.");

        run.StartExecution();

        var startedAt = DateTimeOffset.UtcNow;

        var result = new ExecutionResult(
            Available: false,
            Success: false,
            ExitCode: null,
            StandardOutput: string.Empty,
            StandardError: string.Empty,
            DurationMs: 0,
            TimedOut: false,
            Reason: "EXECUTION_UNAVAILABLE");

        var completedAt = DateTimeOffset.UtcNow;
        run.RecordExecutionAttempt(startedAt, completedAt, result);

        return new ExecutionResponse(
            result.Available,
            result.Success,
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.DurationMs,
            result.TimedOut,
            result.Reason);
    }
}
