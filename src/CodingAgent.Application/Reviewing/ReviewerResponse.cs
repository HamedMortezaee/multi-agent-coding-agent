namespace CodingAgent.Application.Reviewing;

public sealed record ReviewerResponse(
    bool Success,
    IReadOnlyCollection<ReviewerIssue> Issues,
    string Summary,
    string NextAction);

public sealed record ReviewerIssue(
    string Severity,
    string Type,
    string? File,
    string Message,
    string? Evidence);
