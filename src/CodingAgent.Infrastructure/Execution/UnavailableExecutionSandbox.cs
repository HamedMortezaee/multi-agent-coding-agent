using CodingAgent.Application.Abstractions;
using CodingAgent.Domain;

namespace CodingAgent.Infrastructure.Execution;

public sealed class UnavailableExecutionSandbox : IExecutionSandbox
{
    public Task<ExecutionResult> ExecuteAsync(
        Guid executionId,
        IReadOnlyCollection<ProjectFile> files,
        string command,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ExecutionResult(
            Available: false,
            Success: false,
            ExitCode: null,
            StandardOutput: string.Empty,
            StandardError: string.Empty,
            DurationMs: 0,
            TimedOut: false,
            Reason: "EXECUTION_UNAVAILABLE"));
    }
}
