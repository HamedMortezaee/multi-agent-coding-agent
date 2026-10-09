using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CodingAgent.Application.Abstractions;

namespace CodingAgent.Infrastructure.Aifa;

public sealed class AifaLlmService(
    HttpClient httpClient,
    AifaOptions options) : ILlmService
{
    public async Task<string> GenerateTextAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Token))
            throw new InvalidOperationException("AIFA token is not configured.");

        if (string.IsNullOrWhiteSpace(options.Model))
            throw new InvalidOperationException("AIFA model is not configured.");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "v1/chat/completions");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", options.Token);

        request.Headers.Add("x-request-id", Guid.NewGuid().ToString("N"));
        request.Headers.Add("x-session-id", Guid.NewGuid().ToString("N"));
        request.Headers.Add(
            "x-user-id",
            string.IsNullOrWhiteSpace(options.UserId)
                ? "coding-agent"
                : options.UserId);

        request.Content = JsonContent.Create(new
        {
            model = options.Model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },
                new
                {
                    role = "user",
                    content = userPrompt
                }
            }
        });

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"AIFA returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {rawBody}");
        }

        using var json = JsonDocument.Parse(rawBody);

        if (!TryGetAssistantContent(json.RootElement, out var content) ||
            string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                $"AIFA returned an unexpected or empty response. Body: {rawBody}");
        }

        return content;
    }

    private static bool TryGetAssistantContent(
        JsonElement root,
        out string? content)
    {
        content = null;

        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return false;
        }

        var firstChoice = choices[0];

        if (!firstChoice.TryGetProperty("message", out var message) ||
            message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var contentElement))
        {
            return false;
        }

        if (contentElement.ValueKind == JsonValueKind.String)
        {
            content = contentElement.GetString();
            return true;
        }

        return false;
    }
}
