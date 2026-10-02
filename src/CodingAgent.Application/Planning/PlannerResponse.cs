namespace CodingAgent.Application.Planning;

public sealed record PlannerResponse(
    string Goal,
    string Summary,
    IReadOnlyCollection<PlannerStep> Steps,
    IReadOnlyCollection<string> Assumptions,
    IReadOnlyCollection<string> Ambiguities,
    bool RequiresHumanReview,
    string? HumanReviewReason);

public sealed record PlannerStep(
    int Order,
    string Title,
    string Description);
