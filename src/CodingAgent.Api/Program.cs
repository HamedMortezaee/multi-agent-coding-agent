#pragma warning disable OPENAI001

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
using CodingAgent.Infrastructure.Aifa;
using CodingAgent.Infrastructure.Execution;
using CodingAgent.Infrastructure.Persistence;
using CodingAgent.Infrastructure.Workspaces;
using OpenAI.Responses;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CodingAgent API",
        Version = "v1",
        Description = "Multi-agent coding orchestration API."
    });

    options.AddSecurityDefinition("AgentApiKey", new OpenApiSecurityScheme
    {
        Name = "X-Agent-Api-Key",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "API key required for /api endpoints."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "AgentApiKey"
            }
        }] = Array.Empty<string>()
    });
});

builder.Services.Configure<OpenAiOptions>(
    builder.Configuration.GetSection(OpenAiOptions.SectionName));

var aiProvider = builder.Configuration["AI:Provider"]?.Trim() ?? "OpenAI";

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
var runnerAllowInvalidCertificate =
    builder.Configuration.GetValue<bool>("Runner:AllowInvalidCertificate");

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
        ApiKey = runnerApiKey,
        AllowInvalidCertificate = runnerAllowInvalidCertificate
    };

    builder.Services.AddSingleton(runnerOptions);
    builder.Services.AddSingleton<IExecutionSandbox>(_ =>
    {
        var handler = new HttpClientHandler();

        if (runnerAllowInvalidCertificate)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(normalizedRunnerBaseUrl, UriKind.Absolute),
            Timeout = Timeout.InfiniteTimeSpan
        };

        return new RemoteExecutionSandbox(client, runnerOptions);
    });
}

if (string.Equals(aiProvider, "Aifa", StringComparison.OrdinalIgnoreCase))
{
    var aifaBaseUrl =
        builder.Configuration["Aifa:BaseUrl"]?.Trim()
        ?? "https://aifa-chatbot.dev.dotin.ir/";

    var aifaOptions = new AifaOptions
    {
        BaseUrl = aifaBaseUrl.TrimEnd('/') + "/",
        Model = builder.Configuration["Aifa:Model"]?.Trim()
            ?? "assistance-model",
        Token = builder.Configuration["Aifa:Token"]?.Trim()
            ?? string.Empty,
        UserId = builder.Configuration["Aifa:UserId"]?.Trim()
            ?? "coding-agent"
    };

    if (string.IsNullOrWhiteSpace(aifaOptions.Token))
    {
        throw new InvalidOperationException(
            "Aifa:Token is required when AI:Provider is Aifa.");
    }

    builder.Services.AddSingleton(aifaOptions);
    builder.Services.AddSingleton<ILlmService>(_ =>
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(aifaOptions.BaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromMinutes(5)
        };

        return new AifaLlmService(client, aifaOptions);
    });
}
else if (string.Equals(aiProvider, "OpenAI", StringComparison.OrdinalIgnoreCase))
{
    var openAiBaseUrl =
        builder.Configuration["OpenAI:BaseUrl"]?.Trim()
        ?? "https://api.openai.com/v1/";

    var openAiApiKey =
        builder.Configuration["OpenAI:ApiKey"]?.Trim()
        ?? string.Empty;

    if (string.IsNullOrWhiteSpace(openAiApiKey))
    {
        throw new InvalidOperationException(
            "OpenAI:ApiKey is required when AI:Provider is OpenAI.");
    }

    var openAiClientOptions = new ResponsesClientOptions
    {
        Endpoint = new Uri(openAiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute)
    };

    builder.Services.AddSingleton(
        _ => new ResponsesClient(
            new System.ClientModel.ApiKeyCredential(openAiApiKey),
            openAiClientOptions));

    builder.Services.AddSingleton<ILlmService, OpenAiLlmService>();
}
else
{
    throw new InvalidOperationException(
        $"Unsupported AI provider '{aiProvider}'. Supported providers: OpenAI, Aifa.");
}
builder.Services.AddScoped<CreateRunService>();
builder.Services.AddScoped<PlannerService>();
builder.Services.AddScoped<HumanReviewService>();
builder.Services.AddScoped<CoderService>();
builder.Services.AddScoped<ExecutionService>();
builder.Services.AddScoped<ReviewerService>();
builder.Services.AddScoped<FixerService>();
builder.Services.AddScoped<FinalReportService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "CodingAgent API v1");
    options.RoutePrefix = "swagger";
});

app.UseMiddleware<ApiKeyMiddleware>();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    utcNow = DateTimeOffset.UtcNow
}));

app.MapGet("/api/v1/diagnostics/llm", (
    ILlmService llmService) =>
{
    var isAifa = string.Equals(
        aiProvider,
        "Aifa",
        StringComparison.OrdinalIgnoreCase);

    var effectiveModel = isAifa
        ? builder.Configuration["Aifa:Model"] ?? "assistance-model"
        : builder.Configuration["OpenAI:Model"] ?? string.Empty;

    var effectiveBaseUrl = isAifa
        ? builder.Configuration["Aifa:BaseUrl"]
        : builder.Configuration["OpenAI:BaseUrl"];

    var credentialConfigured = isAifa
        ? !string.IsNullOrWhiteSpace(builder.Configuration["Aifa:Token"])
        : !string.IsNullOrWhiteSpace(builder.Configuration["OpenAI:ApiKey"]);

    return Results.Ok(new
    {
        provider = aiProvider,
        model = effectiveModel,
        baseUrl = effectiveBaseUrl,
        implementation = llmService.GetType().Name,
        tokenConfigured = credentialConfigured
    });
});

app.MapPost("/api/v1/diagnostics/llm/test", async (
    ILlmService llmService,
    CancellationToken cancellationToken) =>
{
    var startedAt = DateTimeOffset.UtcNow;

    try
    {
        var output = await llmService.GenerateTextAsync(
            "You are a connectivity diagnostic. Return only the word OK.",
            "Reply with exactly OK.",
            cancellationToken);

        return Results.Ok(new
        {
            success = true,
            provider = aiProvider,
            output,
            startedAt,
            completedAt = DateTimeOffset.UtcNow
        });
    }
    catch (Exception exception)
    {
        return Results.Json(
            new
            {
                success = false,
                provider = aiProvider,
                error = exception.Message,
                startedAt,
                completedAt = DateTimeOffset.UtcNow
            },
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/api/v1/diagnostics/runner", (
    IExecutionSandbox executionSandbox) =>
{
    var configuredBaseUrl = builder.Configuration["Runner:BaseUrl"];

    return Results.Ok(new
    {
        diagnosticsVersion = "runner-diag-v2",
        configured = !string.IsNullOrWhiteSpace(configuredBaseUrl),
        baseUrl = configuredBaseUrl,
        allowInvalidCertificate = runnerAllowInvalidCertificate,
        implementation = executionSandbox.GetType().Name,
        implementationAssembly = executionSandbox.GetType().Assembly.GetName().Name,
        implementationAssemblyLocation = executionSandbox.GetType().Assembly.Location
    });
});

app.MapPost("/api/v1/diagnostics/runner/execute", async (
    IExecutionSandbox executionSandbox,
    CancellationToken cancellationToken) =>
{
    var executionId = Guid.NewGuid();

    var files = new[]
    {
        new CodingAgent.Domain.ProjectFile
        {
            Path = "DiagnosticApp.csproj",
            Content = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""",
            Version = 1
        },
        new CodingAgent.Domain.ProjectFile
        {
            Path = "Program.cs",
            Content = """
Console.WriteLine("CodingAgent API -> Runner OK");
""",
            Version = 1
        }
    };

    var startedAt = DateTimeOffset.UtcNow;

    var result = await executionSandbox.ExecuteAsync(
        executionId,
        files,
        "dotnet build",
        120,
        cancellationToken);

    return Results.Ok(new
    {
        diagnosticsVersion = "runner-execute-v1",
        executionId,
        startedAt,
        completedAt = DateTimeOffset.UtcNow,
        runner = new
        {
            configuredBaseUrl = builder.Configuration["Runner:BaseUrl"],
            allowInvalidCertificate = runnerAllowInvalidCertificate,
            implementation = executionSandbox.GetType().Name
        },
        result
    });
});

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
