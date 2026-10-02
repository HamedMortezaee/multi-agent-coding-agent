namespace CodingAgent.Application.HumanReview;

public sealed record HumanReviewResponse(
    Guid ExecutionId,
    string Decision,
    string Status,
    string? Feedback);
