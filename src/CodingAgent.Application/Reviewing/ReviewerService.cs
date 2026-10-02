using System.Text.Json;
using CodingAgent.Application.Abstractions;
using CodingAgent.Domain;

namespace CodingAgent.Application.Reviewing;

public sealed class ReviewerService(
    IAgentRunRepository repository,
    ILlmService llmService)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ReviewerResponse> ExecuteAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var run = await repository.GetAsync(executionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Run '{executionId}' was not found.");

        var latestAttempt = run.GetLatestAttempt();
        run.StartReview();

        if (!latestAttempt.ExecutionResult.Available)
        {
            var unavailable = new ReviewerResponse(
                false,
                new[]
                {
                    new ReviewerIssue(
                        "Critical",
                        "ExecutionUnavailable",
                        null,
                        "No code execution environment is configured.",
                        latestAttempt.ExecutionResult.Reason)
                },
                "Generated code could not be executed because no runner is configured.",
                "fail");

            run.SetReview(ToDomain(unavailable));
            await repository.SaveAsync(run, cancellationToken);
            return unavailable;
        }

        var systemPrompt = """
You are the Tester/Reviewer agent of a multi-agent coding system.
Analyze the real execution result and determine whether the generated project succeeded.
Return ONLY valid JSON.

Schema:
{
  "success": false,
  "issues": [
    {
      "severity": "Error",
      "type": "Compilation",
      "file": "relative/path or null",
      "message": "string",
      "evidence": "string or null"
    }
  ],
  "summary": "string",
  "nextAction": "complete|fix|fail"
}

Rules:
- Base conclusions on the real execution result.
- Use nextAction=complete only when execution genuinely succeeded.
- Use nextAction=fix for actionable code problems.
- Use nextAction=fail for non-code infrastructure blockers.
""";

        var userPrompt = JsonSerializer.Serialize(new
        {
            run.UserRequest,
            run.Plan,
            run.Files,
            Execution = latestAttempt.ExecutionResult,
            PreviousAttempts = run.Attempts
        }, JsonOptions);

        var raw = await llmService.GenerateTextAsync(
            systemPrompt,
            userPrompt,
            cancellationToken);

        var response = JsonSerializer.Deserialize<ReviewerResponse>(raw, JsonOptions)
            ?? throw new InvalidOperationException("Reviewer returned an empty response.");

        Validate(response);
        run.SetReview(ToDomain(response));
        await repository.SaveAsync(run, cancellationToken);
        return response;
    }

    private static void Validate(ReviewerResponse response)
    {
        var action = response.NextAction?.Trim().ToLowerInvariant();

        if (action is not ("complete" or "fix" or "fail"))
            throw new InvalidOperationException(
                "Reviewer nextAction must be complete, fix or fail.");

        if (string.IsNullOrWhiteSpace(response.Summary))
            throw new InvalidOperationException("Reviewer summary is required.");
    }

    private static ReviewResult ToDomain(ReviewerResponse response) =>
        new()
        {
            Success = response.Success,
            Summary = response.Summary,
            NextAction = response.NextAction.Trim().ToLowerInvariant(),
            Issues = response.Issues
                .Select(issue => new ReviewIssue(
                    issue.Severity,
                    issue.Type,
                    issue.File,
                    issue.Message,
                    issue.Evidence))
                .ToArray()
        };
}
