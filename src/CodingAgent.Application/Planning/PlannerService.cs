using System.Text.Json;
using CodingAgent.Application.Abstractions;
using CodingAgent.Domain;

namespace CodingAgent.Application.Planning;

public sealed class PlannerService(
    IAgentRunRepository repository,
    ILlmService llmService)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<PlannerResponse> ExecuteAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var run = await repository.GetAsync(executionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Run '{executionId}' was not found.");

        run.StartPlanning();
        await repository.SaveAsync(run, cancellationToken);
        await repository.SaveAsync(run, cancellationToken);

        var systemPrompt = """
You are the Planner agent of a multi-agent coding system.
Analyze the user's software request and return ONLY valid JSON.
Do not include markdown fences or explanatory text.

The JSON schema is:
{
  "goal": "string",
  "summary": "string",
  "steps": [
    {
      "order": 1,
      "title": "string",
      "description": "string"
    }
  ],
  "assumptions": ["string"],
  "ambiguities": ["string"],
  "requiresHumanReview": true,
  "humanReviewReason": "string or null"
}

Rules:
- Target platform is ASP.NET Core.
- Target language is C#.
- Keep the MVP small and testable.
- Break the work into concrete implementation steps.
- For the current MVP, requiresHumanReview must be true.
""";

        var userPrompt = $"""
User request:
{run.UserRequest}

Human feedback from previous review:
{run.HumanFeedback ?? "(none)"}

If human feedback exists, revise the plan to address it explicitly.
""";

        var raw = await llmService.GenerateTextAsync(
            systemPrompt,
            userPrompt,
            cancellationToken);

        var planner = JsonSerializer.Deserialize<PlannerResponse>(raw, JsonOptions)
            ?? throw new InvalidOperationException("Planner returned an empty response.");

        Validate(planner);

        var plan = new AgentPlan
        {
            Goal = planner.Goal.Trim(),
            Summary = planner.Summary.Trim(),
            Steps = planner.Steps
                .OrderBy(step => step.Order)
                .Select(step => new PlanStep(
                    step.Order,
                    step.Title.Trim(),
                    step.Description.Trim()))
                .ToArray(),
            Assumptions = planner.Assumptions.ToArray(),
            Ambiguities = planner.Ambiguities.ToArray(),
            RequiresHumanReview = planner.RequiresHumanReview,
            HumanReviewReason = planner.HumanReviewReason
        };

        run.SetPlan(plan);
        await repository.SaveAsync(run, cancellationToken);

        return planner;
    }

    private static void Validate(PlannerResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Goal))
            throw new InvalidOperationException("Planner goal is required.");

        if (response.Steps.Count == 0)
            throw new InvalidOperationException("Planner must return at least one step.");

        if (response.Steps
            .GroupBy(step => step.Order)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Planner step order values must be unique.");
        }
    }
}
