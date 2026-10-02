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
    public string? HumanFeedback { get; private set; }
    public IReadOnlyCollection<ProjectFile> Files { get; private set; } = Array.Empty<ProjectFile>();

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

    public void ApprovePlan(string? feedback)
    {
        EnsureNotExpired();

        if (Status != AgentRunStatus.WaitingForHuman)
            throw new InvalidOperationException(
                $"Plan cannot be approved while run status is '{Status}'.");

        HumanFeedback = NormalizeFeedback(feedback);
        Status = AgentRunStatus.Coding;
    }

    public void RequestPlanModification(string feedback)
    {
        EnsureNotExpired();

        if (Status != AgentRunStatus.WaitingForHuman)
            throw new InvalidOperationException(
                $"Plan modification cannot be requested while run status is '{Status}'.");

        if (string.IsNullOrWhiteSpace(feedback))
            throw new ArgumentException(
                "Feedback is required when requesting plan modification.",
                nameof(feedback));

        HumanFeedback = feedback.Trim();
        Status = AgentRunStatus.Planning;
    }

    public void RejectPlan(string? feedback)
    {
        EnsureNotExpired();

        if (Status != AgentRunStatus.WaitingForHuman)
            throw new InvalidOperationException(
                $"Plan cannot be rejected while run status is '{Status}'.");

        HumanFeedback = NormalizeFeedback(feedback);
        Status = AgentRunStatus.Failed;
    }

    public void StartCoding()
    {
        EnsureNotExpired();

        if (Status != AgentRunStatus.Coding)
            throw new InvalidOperationException(
                $"Coding cannot start while run status is '{Status}'.");

        if (Plan is null)
            throw new InvalidOperationException("Approved plan is required before coding.");
    }

    public void SetGeneratedFiles(IReadOnlyCollection<ProjectFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        EnsureNotExpired();

        if (Status != AgentRunStatus.Coding)
            throw new InvalidOperationException(
                $"Generated files cannot be set while run status is '{Status}'.");

        if (files.Count == 0)
            throw new ArgumentException(
                "At least one generated file is required.",
                nameof(files));

        Files = files.ToArray();
        Status = AgentRunStatus.WaitingForExecution;
    }

    private void EnsureNotExpired()
    {
        if (DateTimeOffset.UtcNow < Deadline)
            return;

        Status = AgentRunStatus.TimedOut;
        throw new InvalidOperationException("The agent run has timed out.");
    }

    private static string? NormalizeFeedback(string? feedback) =>
        string.IsNullOrWhiteSpace(feedback)
            ? null
            : feedback.Trim();
}
