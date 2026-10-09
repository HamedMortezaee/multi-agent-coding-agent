using CodingAgent.Application.Abstractions;
using CodingAgent.Domain;

namespace CodingAgent.Application.Execution;

public sealed class ExecutionService(
    IAgentRunRepository repository,
    IExecutionSandbox executionSandbox)
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
        await repository.SaveAsync(run, cancellationToken);

        var startedAt = DateTimeOffset.UtcNow;

        var result = await executionSandbox.ExecuteAsync(
            executionId,
            run.Files,
            request.Command,
            request.TimeoutSeconds,
            cancellationToken);

        var completedAt = DateTimeOffset.UtcNow;
        run.RecordExecutionAttempt(startedAt, completedAt, result);
        await repository.SaveAsync(run, cancellationToken);

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
