namespace CodingAgent.Domain;

public sealed class AgentPlan
{
    public string Goal { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyCollection<PlanStep> Steps { get; init; } = Array.Empty<PlanStep>();
    public IReadOnlyCollection<string> Assumptions { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> Ambiguities { get; init; } = Array.Empty<string>();
    public bool RequiresHumanReview { get; init; }
    public string? HumanReviewReason { get; init; }
}

public sealed record PlanStep(
    int Order,
    string Title,
    string Description);
