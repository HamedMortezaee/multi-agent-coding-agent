using System.Text.Json;
using CodingAgent.Application.Abstractions;
using CodingAgent.Domain;

namespace CodingAgent.Application.Fixing;

public sealed class FixerService(
    IAgentRunRepository repository,
    ILlmService llmService,
    IWorkspaceService workspaceService)
{
    private const int MaxFixAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<FixerResponse> ExecuteAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var run = await repository.GetAsync(executionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Run '{executionId}' was not found.");

        run.StartFix(MaxFixAttempts);
        await repository.SaveAsync(run, cancellationToken);

        var attemptNumber = run.FixAttemptCount;

        var systemPrompt = """
You are the Fixer agent of a multi-agent coding system.
Use the approved plan, current files, real execution result, reviewer findings, and previous attempts.
Return ONLY valid JSON.

Schema:
{
  "changes": [
    {
      "path": "relative/path",
      "operation": "update|create|delete",
      "content": "complete replacement content; empty only for delete",
      "reason": "string"
    }
  ],
  "summary": "string"
}

Rules:
- Fix only issues supported by the execution/review evidence.
- Do not silently expand product scope.
- Avoid repeating unsuccessful previous fixes.
- Paths must be relative and safe.
""";

        var userPrompt = JsonSerializer.Serialize(new
        {
            run.UserRequest,
            run.Plan,
            run.HumanFeedback,
            CurrentFiles = run.Files,
            LatestAttempt = run.GetLatestAttempt(),
            PreviousAttempts = run.Attempts,
            FixAttemptNumber = attemptNumber
        }, JsonOptions);

        var raw = await llmService.GenerateTextAsync(
            systemPrompt,
            userPrompt,
            cancellationToken);

        var payload = JsonSerializer.Deserialize<FixerPayload>(raw, JsonOptions)
            ?? throw new InvalidOperationException("Fixer returned an empty response.");

        Validate(payload);

        var changes = payload.Changes
            .Select(change => new FixerChange(
                NormalizePath(change.Path),
                change.Operation.Trim().ToLowerInvariant(),
                change.Content ?? string.Empty,
                change.Reason.Trim()))
            .ToArray();

        run.ApplyFix(
            changes.Select(change => new ProjectFileChange(
                change.Path,
                change.Operation,
                change.Content,
                change.Reason)).ToArray(),
            payload.Summary);

        await workspaceService.MaterializeAsync(
            executionId,
            run.Files,
            cancellationToken);

        await repository.SaveAsync(run, cancellationToken);

        return new FixerResponse(
            attemptNumber,
            changes,
            payload.Summary);
    }

    private static void Validate(FixerPayload payload)
    {
        if (payload.Changes.Count == 0)
            throw new InvalidOperationException("Fixer must return at least one change.");

        foreach (var change in payload.Changes)
        {
            var operation = change.Operation?.Trim().ToLowerInvariant();

            if (operation is not ("create" or "update" or "delete"))
                throw new InvalidOperationException(
                    "Fixer operation must be create, update or delete.");

            _ = NormalizePath(change.Path);

            if (string.IsNullOrWhiteSpace(change.Reason))
                throw new InvalidOperationException("Fixer change reason is required.");
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Fixer file path is required.");

        var normalized = path.Trim().Replace('\\', '/');

        if (normalized.StartsWith('/') ||
            Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(segment => segment == ".."))
        {
            throw new InvalidOperationException(
                $"Unsafe file path returned by Fixer: '{path}'.");
        }

        return normalized;
    }

    private sealed record FixerPayload(
        IReadOnlyCollection<FixerChangePayload> Changes,
        string Summary);

    private sealed record FixerChangePayload(
        string Path,
        string Operation,
        string Content,
        string Reason);
}
