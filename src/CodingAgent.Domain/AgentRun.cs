namespace CodingAgent.Domain;

public sealed class AgentRun
{
    private AgentRun() { }

    private AgentRun(
        Guid executionId,
        string userRequest,
        string requestedBy,
        DateTimeOffset startedAt,
        DateTimeOffset deadline)
    {
        ExecutionId = executionId;
        UserRequest = userRequest;
        RequestedBy = requestedBy;
        StartedAt = startedAt;
        Deadline = deadline;
        Status = AgentRunStatus.Created;
    }

    public Guid ExecutionId { get; private set; }
    public string UserRequest { get; private set; } = string.Empty;
    public string RequestedBy { get; private set; } = string.Empty;
    public AgentRunStatus Status { get; private set; }
    public int FixAttemptCount { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset Deadline { get; private set; }
    public AgentPlan? Plan { get; private set; }

    public static AgentRun Create(
        string userRequest,
        string requestedBy,
        DateTimeOffset now,
        TimeSpan maxRunDuration)
    {
        if (string.IsNullOrWhiteSpace(userRequest))
            throw new ArgumentException("User request is required.", nameof(userRequest));

        if (string.IsNullOrWhiteSpace(requestedBy))
            requestedBy = "user";

        if (maxRunDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxRunDuration));

        return new AgentRun(
            Guid.NewGuid(),
            userRequest.Trim(),
            requestedBy.Trim(),
            now,
            now.Add(maxRunDuration));
    }

    public void StartPlanning()
    {
        EnsureNotExpired();

        if (Status is not AgentRunStatus.Created and not AgentRunStatus.Planning)
            throw new InvalidOperationException(
                $"Run cannot enter Planning from '{Status}'.");

        Status = AgentRunStatus.Planning;
    }

    public void SetPlan(AgentPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnsureNotExpired();

        if (Status != AgentRunStatus.Planning)
            throw new InvalidOperationException(
                $"Plan cannot be set while run status is '{Status}'.");

        Plan = plan;
        Status = plan.RequiresHumanReview
            ? AgentRunStatus.WaitingForHuman
            : AgentRunStatus.Coding;
    }

    private void EnsureNotExpired()
    {
        if (DateTimeOffset.UtcNow < Deadline)
            return;

        Status = AgentRunStatus.TimedOut;
        throw new InvalidOperationException("The agent run has timed out.");
    }
}
