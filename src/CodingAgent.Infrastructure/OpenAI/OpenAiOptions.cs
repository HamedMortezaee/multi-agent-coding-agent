namespace CodingAgent.Infrastructure.OpenAI;

public sealed class OpenAiOptions
{
    public const string SectionName = "AI";

    public string Provider { get; init; } = "OpenAI";
    public string Model { get; init; } = string.Empty;
}
