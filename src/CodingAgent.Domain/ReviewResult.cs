namespace CodingAgent.Domain;

public sealed class ReviewResult
{
    public bool Success { get; init; }
    public IReadOnlyCollection<ReviewIssue> Issues { get; init; } = Array.Empty<ReviewIssue>();
    public string Summary { get; init; } = string.Empty;
    public string NextAction { get; init; } = string.Empty;
}

public sealed record ReviewIssue(
    string Severity,
    string Type,
    string? File,
    string Message,
    string? Evidence);
