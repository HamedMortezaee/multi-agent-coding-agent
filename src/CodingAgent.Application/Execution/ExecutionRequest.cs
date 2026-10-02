namespace CodingAgent.Application.Execution;

public sealed record ExecutionRequest(
    string Command,
    int TimeoutSeconds = 60);
