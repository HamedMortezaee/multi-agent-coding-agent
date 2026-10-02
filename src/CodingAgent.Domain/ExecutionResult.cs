namespace CodingAgent.Domain;

public sealed record ExecutionResult(
    bool Available,
    bool Success,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    long DurationMs,
    bool TimedOut,
    string? Reason);
