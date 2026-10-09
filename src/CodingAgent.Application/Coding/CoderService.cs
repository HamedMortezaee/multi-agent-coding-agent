using System.Text.Json;
using CodingAgent.Application.Abstractions;
using CodingAgent.Domain;

namespace CodingAgent.Application.Coding;

public sealed class CoderService(
    IAgentRunRepository repository,
    ILlmService llmService,
    IWorkspaceService workspaceService)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<CoderResponse> ExecuteAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var run = await repository.GetAsync(executionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Run '{executionId}' was not found.");

        run.StartCoding();
        await repository.SaveAsync(run, cancellationToken);

        if (run.Plan is null)
            throw new InvalidOperationException("Approved plan is required before coding.");

        var systemPrompt = """
You are the Coder agent of a multi-agent coding system.
Generate a small, complete ASP.NET Core Web API project based strictly on the approved plan.
Return ONLY valid JSON. Do not include markdown fences or explanatory text outside JSON.

The JSON schema is:
{
  "projectName": "string",
  "files": [
    {
      "path": "relative/path",
      "content": "complete file content",
      "operation": "create"
    }
  ],
  "notes": ["string"]
}

Rules:
- Target platform: ASP.NET Core.
- Target language: C#.
- Keep the project small and appropriate for an MVP.
- Every required source/config/project file must be returned with complete contents.
- File paths must be relative.
- Never use '..' in a file path.
- Do not return absolute paths.
- For the initial generation, operation must be 'create'.
- Do not silently add features outside the approved plan and human feedback.
""";

        var planJson = JsonSerializer.Serialize(run.Plan, JsonOptions);

        var userPrompt = $"""
Original user request:
{run.UserRequest}

Approved plan:
{planJson}

Human feedback:
{run.HumanFeedback ?? "(none)"}
""";

        var raw = await llmService.GenerateTextAsync(
            systemPrompt,
            userPrompt,
            cancellationToken);

        var result = JsonSerializer.Deserialize<CoderResponse>(raw, JsonOptions)
            ?? throw new InvalidOperationException("Coder returned an empty response.");

        Validate(result);

        var files = result.Files
            .Select(file => new ProjectFile
            {
                Path = file.Path.Trim().Replace('\\', '/'),
                Content = file.Content,
                Version = 1
            })
            .ToArray();

        run.SetGeneratedFiles(files);

        await workspaceService.MaterializeAsync(
            executionId,
            run.Files,
            cancellationToken);

        await repository.SaveAsync(run, cancellationToken);

        return result;
    }

    private static void Validate(CoderResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.ProjectName))
            throw new InvalidOperationException("Coder projectName is required.");

        if (response.Files.Count == 0)
            throw new InvalidOperationException("Coder must return at least one file.");

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in response.Files)
        {
            if (!string.Equals(file.Operation, "create", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Initial Coder output only supports operation 'create'.");

            if (string.IsNullOrWhiteSpace(file.Path))
                throw new InvalidOperationException("Coder file path is required.");

            var path = file.Path.Trim().Replace('\\', '/');

            if (path.StartsWith('/') ||
                Path.IsPathRooted(path) ||
                path.Split('/').Any(segment => segment == ".."))
            {
                throw new InvalidOperationException(
                    $"Unsafe file path returned by Coder: '{file.Path}'.");
            }

            if (!normalized.Add(path))
                throw new InvalidOperationException(
                    $"Duplicate file path returned by Coder: '{file.Path}'.");
        }
    }
}
