using CodingAgent.Domain;

namespace CodingAgent.Application.Abstractions;

public interface IWorkspaceService
{
    Task<WorkspaceMaterializationResult> MaterializeAsync(
        Guid executionId,
        IReadOnlyCollection<ProjectFile> files,
        CancellationToken cancellationToken = default);
}

public sealed record WorkspaceMaterializationResult(
    string RelativePath,
    int FileCount);
