namespace CodingAgent.Application.Execution;

public sealed record ExecutionResponse(
    bool Available,
    bool Success,
    int? ExitCode,
    string Stdout,
    string Stderr,
    long DurationMs,
    bool TimedOut,
    string? Reason);
