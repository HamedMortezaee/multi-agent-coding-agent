namespace CodingAgent.Infrastructure.Aifa;

public sealed class AifaOptions
{
    public const string SectionName = "Aifa";

    public string BaseUrl { get; init; } = "https://aifa-chatbot.dev.dotin.ir/";
    public string Model { get; init; } = "assistance-model";
    public string Token { get; init; } = string.Empty;
    public string UserId { get; init; } = "coding-agent";
}
