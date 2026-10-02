using CodingAgent.Domain;

namespace CodingAgent.Infrastructure.Persistence;

public sealed record AgentRunSnapshot(
    Guid ExecutionId,
    string UserRequest,
    string RequestedBy,
    AgentRunStatus Status,
    int FixAttemptCount,
    DateTimeOffset StartedAt,
    DateTimeOffset Deadline,
    AgentPlan? Plan,
    string? HumanFeedback,
    IReadOnlyCollection<ProjectFile> Files,
    IReadOnlyCollection<ExecutionAttempt> Attempts)
{
    public static AgentRunSnapshot FromDomain(AgentRun run) =>
        new(
            run.ExecutionId,
            run.UserRequest,
            run.RequestedBy,
            run.Status,
            run.FixAttemptCount,
            run.StartedAt,
            run.Deadline,
            run.Plan,
            run.HumanFeedback,
            run.Files,
            run.Attempts);

    public AgentRun ToDomain() =>
        AgentRun.Restore(
            ExecutionId,
            UserRequest,
            RequestedBy,
            Status,
            FixAttemptCount,
            StartedAt,
            Deadline,
            Plan,
            HumanFeedback,
            Files,
            Attempts);
}
