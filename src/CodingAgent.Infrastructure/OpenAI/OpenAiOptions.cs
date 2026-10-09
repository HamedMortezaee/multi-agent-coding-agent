namespace CodingAgent.Infrastructure.OpenAI;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string BaseUrl { get; init; } = "https://api.openai.com/v1/";
    public string Model { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
}
