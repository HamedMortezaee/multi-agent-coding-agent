namespace CodingAgent.Application.Runs.CreateRun;

public sealed record CreateRunResponse(
    Guid ExecutionId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset Deadline);
