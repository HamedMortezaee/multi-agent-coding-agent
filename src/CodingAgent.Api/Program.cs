#pragma warning disable OPENAI001

using CodingAgent.Api.Contracts;
using CodingAgent.Application.Abstractions;
using CodingAgent.Application.Planning;
using CodingAgent.Application.Runs.CreateRun;
using CodingAgent.Infrastructure.OpenAI;
using CodingAgent.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OpenAiOptions>(
    builder.Configuration.GetSection(OpenAiOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAgentRunRepository, InMemoryAgentRunRepository>();

builder.Services.AddSingleton(sp =>
{
    var apiKey = builder.Configuration["OPENAI_API_KEY"]
        ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    if (string.IsNullOrWhiteSpace(apiKey))
        throw new InvalidOperationException(
            "OpenAI API key is not configured. Set OPENAI_API_KEY.");

    return new ResponsesClient(apiKey);
});

builder.Services.AddSingleton<ILlmService, OpenAiLlmService>();
builder.Services.AddScoped<CreateRunService>();
builder.Services.AddScoped<PlannerService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    utcNow = DateTimeOffset.UtcNow
}));

app.MapPost("/api/v1/runs", async (
    CreateRunRequest request,
    CreateRunService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.ExecuteAsync(request, cancellationToken);

        return Results.Created(
            $"/api/v1/runs/{result.ExecutionId}",
            new ApiResponse<CreateRunResponse>(
                true,
                result,
                Array.Empty<ApiError>(),
                new ApiMeta(result.ExecutionId, DateTimeOffset.UtcNow)));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(
            new ApiResponse<object>(
                false,
                null,
                new[]
                {
                    new ApiError(
                        "VALIDATION_ERROR",
                        exception.Message)
                },
                new ApiMeta(null, DateTimeOffset.UtcNow)));
    }
});

app.MapGet("/api/v1/runs/{executionId:guid}", async (
    Guid executionId,
    IAgentRunRepository repository,
    CancellationToken cancellationToken) =>
{
    var run = await repository.GetAsync(executionId, cancellationToken);

    if (run is null)
    {
        return Results.NotFound(
            new ApiResponse<object>(
                false,
                null,
                new[]
                {
                    new ApiError(
                        "RUN_NOT_FOUND",
                        $"Run '{executionId}' was not found.")
                },
                new ApiMeta(executionId, DateTimeOffset.UtcNow)));
    }

    var data = new
    {
        run.ExecutionId,
        run.UserRequest,
        run.RequestedBy,
        status = run.Status.ToString(),
        run.FixAttemptCount,
        run.StartedAt,
        run.Deadline,
        run.Plan
    };

    return Results.Ok(
        new ApiResponse<object>(
            true,
            data,
            Array.Empty<ApiError>(),
            new ApiMeta(executionId, DateTimeOffset.UtcNow)));
});

app.MapPost("/api/v1/runs/{executionId:guid}/plan", async (
    Guid executionId,
    PlannerService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.ExecuteAsync(
            executionId,
            cancellationToken);

        return Results.Ok(
            new ApiResponse<PlannerResponse>(
                true,
                result,
                Array.Empty<ApiError>(),
                new ApiMeta(executionId, DateTimeOffset.UtcNow)));
    }
    catch (KeyNotFoundException exception)
    {
        return Results.NotFound(
            new ApiResponse<object>(
                false,
                null,
                new[]
                {
                    new ApiError(
                        "RUN_NOT_FOUND",
                        exception.Message)
                },
                new ApiMeta(executionId, DateTimeOffset.UtcNow)));
    }
    catch (InvalidOperationException exception)
    {
        return Results.UnprocessableEntity(
            new ApiResponse<object>(
                false,
                null,
                new[]
                {
                    new ApiError(
                        "PLANNER_ERROR",
                        exception.Message)
                },
                new ApiMeta(executionId, DateTimeOffset.UtcNow)));
    }
});

app.Run();

public partial class Program;
