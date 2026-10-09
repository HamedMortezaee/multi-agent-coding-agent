#pragma warning disable OPENAI001

using CodingAgent.Api.Configuration;
using CodingAgent.Api.Contracts;
using CodingAgent.Api.Security;
using CodingAgent.Application.Abstractions;
using CodingAgent.Application.Coding;
using CodingAgent.Application.Execution;
using CodingAgent.Application.Fixing;
using CodingAgent.Application.HumanReview;
using CodingAgent.Application.Planning;
using CodingAgent.Application.Reviewing;
using CodingAgent.Application.Reporting;
using CodingAgent.Application.Runs.CreateRun;
using CodingAgent.Infrastructure.OpenAI;
using CodingAgent.Infrastructure.Execution;
using CodingAgent.Infrastructure.Persistence;
using CodingAgent.Infrastructure.Workspaces;
using OpenAI.Responses;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OpenAiOptions>(
    builder.Configuration.GetSection(OpenAiOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
var persistenceRoot = builder.Configuration["Persistence:RootPath"];

if (string.IsNullOrWhiteSpace(persistenceRoot))
{
    persistenceRoot = Path.Combine(
        AppContext.BaseDirectory,
        "App_Data",
        "agent-runs");
}
else if (!Path.IsPathRooted(persistenceRoot))
{
    persistenceRoot = Path.Combine(
        AppContext.BaseDirectory,
        persistenceRoot);
}

builder.Services.AddSingleton<IAgentRunRepository>(
    _ => new JsonFileAgentRunRepository(persistenceRoot));

var workspaceRoot = builder.Configuration["Workspace:RootPath"];

if (string.IsNullOrWhiteSpace(workspaceRoot))
{
    workspaceRoot = Path.Combine(
        AppContext.BaseDirectory,
        "App_Data",
        "workspaces");
}
else if (!Path.IsPathRooted(workspaceRoot))
{
    workspaceRoot = Path.Combine(
        AppContext.BaseDirectory,
        workspaceRoot);
}

builder.Services.AddSingleton<IWorkspaceService>(
    _ => new FileSystemWorkspaceService(workspaceRoot));

var runnerBaseUrl = builder.Configuration["Runner:BaseUrl"];
var runnerApiKey = builder.Configuration["Runner:ApiKey"] ?? string.Empty;

if (string.IsNullOrWhiteSpace(runnerBaseUrl))
{
    builder.Services.AddSingleton<IExecutionSandbox, UnavailableExecutionSandbox>();
}
else
{
    var normalizedRunnerBaseUrl = runnerBaseUrl.TrimEnd('/') + "/";
    var runnerOptions = new RemoteRunnerOptions
    {
        BaseUrl = normalizedRunnerBaseUrl,
        ApiKey = runnerApiKey
    };

    builder.Services.AddSingleton(runnerOptions);
    builder.Services.AddSingleton(_ => new HttpClient
    {
        BaseAddress = new Uri(normalizedRunnerBaseUrl, UriKind.Absolute),
        Timeout = Timeout.InfiniteTimeSpan
    });
    builder.Services.AddSingleton<IExecutionSandbox, RemoteExecutionSandbox>();
}

builder.Services.AddSingleton(
    _ => new ResponsesClient(TemporarySecrets.OpenAiApiKey));

builder.Services.AddSingleton<ILlmService, OpenAiLlmService>();
builder.Services.AddScoped<CreateRunService>();
builder.Services.AddScoped<PlannerService>();
builder.Services.AddScoped<HumanReviewService>();
builder.Services.AddScoped<CoderService>();
builder.Services.AddScoped<ExecutionService>();
builder.Services.AddScoped<ReviewerService>();
builder.Services.AddScoped<FixerService>();
builder.Services.AddScoped<FinalReportService>();

var app = builder.Build();

app.UseMiddleware<ApiKeyMiddleware>();

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
        return ValidationError(null, exception.Message);
    }
});

app.MapGet("/api/v1/runs/{executionId:guid}", async (
    Guid executionId,
    IAgentRunRepository repository,
    CancellationToken cancellationToken) =>
{
    var run = await repository.GetAsync(executionId, cancellationToken);

    if (run is null)
        return RunNotFound(executionId);

    var data = new
    {
        run.ExecutionId,
        run.UserRequest,
        run.RequestedBy,
        status = run.Status.ToString(),
        run.FixAttemptCount,
        run.StartedAt,
        run.Deadline,
        run.Plan,
        run.HumanFeedback,
        run.Files
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
    catch (KeyNotFoundException)
    {
        return RunNotFound(executionId);
    }
    catch (InvalidOperationException exception)
    {
        return DomainError(
            executionId,
            "PLANNER_ERROR",
            exception.Message);
    }
    catch (Exception exception)
    {
        return Results.Json(
            new ApiResponse<object>(
                false,
                null,
                new[]
                {
                    new ApiError(
                        "LLM_PROVIDER_ERROR",
                        exception.Message,
                        Retryable: false)
                },
                new ApiMeta(executionId, DateTimeOffset.UtcNow)),
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapPost("/api/v1/runs/{executionId:guid}/human-review", async (
    Guid executionId,
    HumanReviewRequest request,
    HumanReviewService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.ExecuteAsync(
            executionId,
            request,
            cancellationToken);

        return Results.Ok(
            new ApiResponse<HumanReviewResponse>(
                true,
                result,
                Array.Empty<ApiError>(),
                new ApiMeta(executionId, DateTimeOffset.UtcNow)));
    }
    catch (KeyNotFoundException)
    {
        return RunNotFound(executionId);
    }
    catch (ArgumentException exception)
    {
        return ValidationError(executionId, exception.Message);
    }
    catch (InvalidOperationException exception)
    {
        return ConflictError(
            executionId,
            "INVALID_RUN_STATE",
            exception.Message);
    }
});

app.MapPost("/api/v1/runs/{executionId:guid}/code", async (
    Guid executionId,
    CoderService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.ExecuteAsync(
            executionId,
            cancellationToken);

        return Results.Ok(
            new ApiResponse<CoderResponse>(
                true,
                result,
                Array.Empty<ApiError>(),
                new ApiMeta(executionId, DateTimeOffset.UtcNow)));
    }
    catch (KeyNotFoundException)
    {
        return RunNotFound(executionId);
    }
    catch (InvalidOperationException exception)
    {
        return DomainError(
            executionId,
            "CODER_ERROR",
            exception.Message);
    }
});

app.MapPost("/api/v1/runs/{executionId:guid}/execute", async (
    Guid executionId,
    ExecutionRequest request,
    ExecutionService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.ExecuteAsync(
            executionId,
            request,
            cancellationToken);

        if (!result.Available)
        {
            return Results.Json(
                new ApiResponse<ExecutionResponse>(
                    false,
                    result,
                    new[]
                    {
                        new ApiError(
                            "EXECUTION_UNAVAILABLE",
                            "No code execution environment is configured.",
                            Retryable: false)
                    },
                    new ApiMeta(executionId, DateTimeOffset.UtcNow)),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(
            new ApiResponse<ExecutionResponse>(
                true,
                result,
                Array.Empty<ApiError>(),
                new ApiMeta(executionId, DateTimeOffset.UtcNow)));
    }
    catch (KeyNotFoundException)
    {
        return RunNotFound(executionId);
    }
    catch (ArgumentOutOfRangeException exception)
    {
        return ValidationError(executionId, exception.Message);
    }
    catch (InvalidOperationException exception)
    {
        return ConflictError(
            executionId,
            "INVALID_RUN_STATE",
            exception.Message);
    }
});

app.MapPost("/api/v1/runs/{executionId:guid}/review", async (
    Guid executionId,
    ReviewerService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.ExecuteAsync(
            executionId,
            cancellationToken);

        return Results.Ok(
            new ApiResponse<ReviewerResponse>(
                true,
                result,
                Array.Empty<ApiError>(),
                new ApiMeta(executionId, DateTimeOffset.UtcNow)));
    }
    catch (KeyNotFoundException)
    {
        return RunNotFound(executionId);
    }
    catch (InvalidOperationException exception)
    {
        return DomainError(
            executionId,
            "REVIEWER_ERROR",
            exception.Message);
    }
});

app.MapPost("/api/v1/runs/{executionId:guid}/fix", async (
    Guid executionId,
    FixerService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.ExecuteAsync(
            executionId,
            cancellationToken);

        return Results.Ok(
            new ApiResponse<FixerResponse>(
                true,
                result,
                Array.Empty<ApiError>(),
                new ApiMeta(executionId, DateTimeOffset.UtcNow)));
    }
    catch (KeyNotFoundException)
    {
        return RunNotFound(executionId);
    }
    catch (InvalidOperationException exception)
    {
        var code = exception.Message.Contains(
            "Maximum fix attempts",
            StringComparison.OrdinalIgnoreCase)
            ? "MAX_FIX_ATTEMPTS_REACHED"
            : "FIXER_ERROR";

        return DomainError(
            executionId,
            code,
            exception.Message);
    }
});

app.MapPost("/api/v1/runs/{executionId:guid}/report", async (
    Guid executionId,
    FinalReportService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.ExecuteAsync(executionId, cancellationToken);

        return Results.Ok(
            new ApiResponse<FinalReportResponse>(
                true,
                result,
                Array.Empty<ApiError>(),
                new ApiMeta(executionId, DateTimeOffset.UtcNow)));
    }
    catch (KeyNotFoundException)
    {
        return RunNotFound(executionId);
    }
});

app.MapGet("/api/v1/runs/{executionId:guid}/report", async (
    Guid executionId,
    FinalReportService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.ExecuteAsync(executionId, cancellationToken);

        return Results.Ok(
            new ApiResponse<FinalReportResponse>(
                true,
                result,
                Array.Empty<ApiError>(),
                new ApiMeta(executionId, DateTimeOffset.UtcNow)));
    }
    catch (KeyNotFoundException)
    {
        return RunNotFound(executionId);
    }
});

app.MapGet("/api/v1/runs/{executionId:guid}/attempts", async (
    Guid executionId,
    IAgentRunRepository repository,
    CancellationToken cancellationToken) =>
{
    var run = await repository.GetAsync(executionId, cancellationToken);

    if (run is null)
        return RunNotFound(executionId);

    return Results.Ok(
        new ApiResponse<object>(
            true,
            new { attempts = run.Attempts },
            Array.Empty<ApiError>(),
            new ApiMeta(executionId, DateTimeOffset.UtcNow)));
});

app.Run();

static IResult RunNotFound(Guid executionId) =>
    Results.NotFound(
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

static IResult ValidationError(Guid? executionId, string message) =>
    Results.BadRequest(
        new ApiResponse<object>(
            false,
            null,
            new[]
            {
                new ApiError(
                    "VALIDATION_ERROR",
                    message)
            },
            new ApiMeta(executionId, DateTimeOffset.UtcNow)));

static IResult ConflictError(
    Guid executionId,
    string code,
    string message) =>
    Results.Conflict(
        new ApiResponse<object>(
            false,
            null,
            new[]
            {
                new ApiError(code, message)
            },
            new ApiMeta(executionId, DateTimeOffset.UtcNow)));

static IResult DomainError(
    Guid executionId,
    string code,
    string message) =>
    Results.UnprocessableEntity(
        new ApiResponse<object>(
            false,
            null,
            new[]
            {
                new ApiError(code, message)
            },
            new ApiMeta(executionId, DateTimeOffset.UtcNow)));

public partial class Program;
