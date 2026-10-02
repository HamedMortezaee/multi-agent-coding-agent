#pragma warning disable OPENAI001

using CodingAgent.Application.Abstractions;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

namespace CodingAgent.Infrastructure.OpenAI;

public sealed class OpenAiLlmService(
    ResponsesClient client,
    IOptions<OpenAiOptions> options) : ILlmService
{
    public async Task<string> GenerateTextAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var model = options.Value.Model;

        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("AI model is not configured.");

        var prompt = $"""
SYSTEM INSTRUCTIONS:
{systemPrompt}

USER REQUEST:
{userPrompt}
""";

        var response = await client.CreateResponseAsync(
            model,
            prompt,
            cancellationToken);

        return response.Value.GetOutputText();
    }
}
