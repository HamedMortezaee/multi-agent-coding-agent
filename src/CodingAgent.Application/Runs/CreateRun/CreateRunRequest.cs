namespace CodingAgent.Application.Runs.CreateRun;

public sealed record CreateRunRequest(
    string Request,
    string? RequestedBy);
