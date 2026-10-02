using System.Security.Cryptography;
using System.Text;

namespace CodingAgent.Api.Security;

public sealed class ApiKeyMiddleware(
    RequestDelegate next,
    IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var configuredKey =
            Environment.GetEnvironmentVariable("AGENT_API_KEY")
            ?? configuration["Security:ApiKey"];

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                errors = new[]
                {
                    new
                    {
                        code = "API_KEY_NOT_CONFIGURED",
                        message = "Agent API key is not configured."
                    }
                }
            });
            return;
        }

        if (!context.Request.Headers.TryGetValue(
                "X-Agent-Api-Key",
                out var providedHeader))
        {
            await Unauthorized(context);
            return;
        }

        var providedKey = providedHeader.ToString();

        if (!FixedTimeEquals(configuredKey, providedKey))
        {
            await Unauthorized(context);
            return;
        }

        await next(context);
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);

        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static async Task Unauthorized(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            errors = new[]
            {
                new
                {
                    code = "UNAUTHORIZED",
                    message = "A valid X-Agent-Api-Key header is required."
                }
            }
        });
    }
}
