namespace CodingAgent.Infrastructure.Execution;

public sealed class RemoteRunnerOptions
{
    public const string SectionName = "Runner";

    public string BaseUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public bool AllowInvalidCertificate { get; init; }
}
