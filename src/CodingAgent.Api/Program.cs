using CodingAgent.Api.Contracts;
using CodingAgent.Application.Abstractions;
using CodingAgent.Application.Runs.CreateRun;
using CodingAgent.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAgentRunRepository, InMemoryAgentRunRepository>();
builder.Services.AddScoped<CreateRunService>();

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
        run.Deadline
    };

    return Results.Ok(
        new ApiResponse<object>(
            true,
            data,
            Array.Empty<ApiError>(),
            new ApiMeta(executionId, DateTimeOffset.UtcNow)));
});

app.Run();

public partial class Program;
