namespace CodingAgent.Application.HumanReview;

public sealed record HumanReviewRequest(
    string Decision,
    string? Feedback);
