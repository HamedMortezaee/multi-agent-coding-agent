namespace CodingAgent.Application.Abstractions;

public interface ILlmService
{
    Task<string> GenerateTextAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
