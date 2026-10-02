namespace CodingAgent.Application.Fixing;

public sealed record FixerResponse(
    int FixAttemptNumber,
    IReadOnlyCollection<FixerChange> Changes,
    string Summary);

public sealed record FixerChange(
    string Path,
    string Operation,
    string Content,
    string Reason);
