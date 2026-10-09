using CodingAgent.Domain;

namespace CodingAgent.Application.Abstractions;

public interface IExecutionSandbox
{
    Task<ExecutionResult> ExecuteAsync(
        Guid executionId,
        IReadOnlyCollection<ProjectFile> files,
        string command,
        int timeoutSeconds,
        CancellationToken cancellationToken = default);
}
