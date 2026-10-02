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
    public IReadOnlyCollection<ExecutionAttempt> Attempts { get; private set; } = Array.Empty<ExecutionAttempt>();

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

    public void StartExecution()
    {
        EnsureNotExpired();

        if (Status != AgentRunStatus.WaitingForExecution)
            throw new InvalidOperationException(
                $"Execution cannot start while run status is '{Status}'.");

        Status = AgentRunStatus.Executing;
    }

    public void RecordExecutionAttempt(
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        ExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (Status != AgentRunStatus.Executing)
            throw new InvalidOperationException(
                $"Execution result cannot be recorded while run status is '{Status}'.");

        var attempts = Attempts.ToList();
        attempts.Add(new ExecutionAttempt
        {
            AttemptNumber = attempts.Count + 1,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ExecutionResult = result
        });

        Attempts = attempts;
        Status = AgentRunStatus.Reviewing;
    }

    public ExecutionAttempt GetLatestAttempt()
    {
        return Attempts.LastOrDefault()
            ?? throw new InvalidOperationException("No execution attempt is available.");
    }

    public void StartReview()
    {
        EnsureNotExpired();

        if (Status != AgentRunStatus.Reviewing)
            throw new InvalidOperationException(
                $"Review cannot start while run status is '{Status}'.");
    }

    public void SetReview(ReviewResult review)
    {
        ArgumentNullException.ThrowIfNull(review);

        if (Status != AgentRunStatus.Reviewing)
            throw new InvalidOperationException(
                $"Review cannot be saved while run status is '{Status}'.");

        GetLatestAttempt().Review = review;

        Status = review.NextAction switch
        {
            "complete" when review.Success => AgentRunStatus.Completed,
            "fix" => AgentRunStatus.Fixing,
            "fail" => AgentRunStatus.Failed,
            _ => throw new InvalidOperationException(
                $"Unsupported review action '{review.NextAction}'.")
        };
    }

    public void StartFix(int maxFixAttempts)
    {
        EnsureNotExpired();

        if (Status != AgentRunStatus.Fixing)
            throw new InvalidOperationException(
                $"Fix cannot start while run status is '{Status}'.");

        if (FixAttemptCount >= maxFixAttempts)
        {
            Status = AgentRunStatus.Failed;
            throw new InvalidOperationException("Maximum fix attempts reached.");
        }

        FixAttemptCount++;
    }

    public void ApplyFix(
        IReadOnlyCollection<ProjectFileChange> changes,
        string summary)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (Status != AgentRunStatus.Fixing)
            throw new InvalidOperationException(
                $"Fix cannot be applied while run status is '{Status}'.");

        if (changes.Count == 0)
            throw new ArgumentException(
                "At least one file change is required.",
                nameof(changes));

        var files = Files.ToDictionary(
            file => file.Path,
            StringComparer.OrdinalIgnoreCase);

        foreach (var change in changes)
        {
            if (change.Operation == "create")
            {
                if (files.ContainsKey(change.Path))
                    throw new InvalidOperationException(
                        $"Cannot create existing file '{change.Path}'.");

                files[change.Path] = new ProjectFile
                {
                    Path = change.Path,
                    Content = change.Content,
                    Version = 1
                };
            }
            else if (change.Operation == "update")
            {
                if (!files.TryGetValue(change.Path, out var currentFile))
                    throw new InvalidOperationException(
                        $"Cannot update missing file '{change.Path}'.");

                files[change.Path] = new ProjectFile
                {
                    Path = change.Path,
                    Content = change.Content,
                    Version = currentFile.Version + 1
                };
            }
            else if (change.Operation == "delete")
            {
                if (!files.Remove(change.Path))
                    throw new InvalidOperationException(
                        $"Cannot delete missing file '{change.Path}'.");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported file operation '{change.Operation}'.");
            }
        }

        Files = files.Values
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        GetLatestAttempt().FixSummary = summary;
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
