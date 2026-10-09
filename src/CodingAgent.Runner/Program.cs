using System.Diagnostics;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration
    .GetSection("Runner")
    .Get<RunnerOptions>() ?? new RunnerOptions();

var workspaceRoot = options.WorkspaceRootPath;
if (!Path.IsPathRooted(workspaceRoot))
    workspaceRoot = Path.Combine(AppContext.BaseDirectory, workspaceRoot);

workspaceRoot = Path.GetFullPath(workspaceRoot);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "CodingAgent.Runner",
    status = "ok"
}));

app.MapGet("/health", () =>
{
    var workspaceExists = Directory.Exists(workspaceRoot);
    var workspaceWritable = false;
    string? workspaceError = null;

    try
    {
        Directory.CreateDirectory(workspaceRoot);
        var probePath = Path.Combine(workspaceRoot, $".write-probe-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(probePath, "ok");
        File.Delete(probePath);
        workspaceWritable = true;
    }
    catch (Exception exception)
    {
        workspaceError = exception.Message;
    }

    return Results.Ok(new
    {
        status = "ok",
        workspaceRoot,
        workspaceExists = Directory.Exists(workspaceRoot),
        workspaceWritable,
        workspaceError
    });
});


app.MapGet("/api/v1/diagnostics/dotnet", async (
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (!string.IsNullOrWhiteSpace(options.ApiKey))
    {
        var supplied = httpContext.Request.Headers["X-Runner-Api-Key"].ToString();
        if (!FixedTimeEquals(supplied, options.ApiKey))
            return Results.Unauthorized();
    }

    var startedAt = Stopwatch.StartNew();

    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };

    process.StartInfo.ArgumentList.Add("--info");

    var diagnosticRoot = Path.Combine(workspaceRoot, ".diagnostics");
    ConfigureDotnetEnvironment(process.StartInfo, diagnosticRoot);

    try
    {
        if (!process.Start())
        {
            return Results.Ok(new
            {
                processAvailable = false,
                success = false,
                exitCode = (int?)null,
                stdout = string.Empty,
                stderr = "dotnet process could not be started.",
                durationMs = startedAt.ElapsedMilliseconds,
                reason = "DOTNET_PROCESS_START_FAILED"
            });
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return Results.Ok(new
        {
            processAvailable = true,
            success = process.ExitCode == 0,
            exitCode = (int?)process.ExitCode,
            stdout,
            stderr,
            durationMs = startedAt.ElapsedMilliseconds,
            reason = process.ExitCode == 0 ? null : "DOTNET_INFO_FAILED"
        });
    }
    catch (Exception exception)
    {
        return Results.Ok(new
        {
            processAvailable = false,
            success = false,
            exitCode = (int?)null,
            stdout = string.Empty,
            stderr = exception.Message,
            durationMs = startedAt.ElapsedMilliseconds,
            reason = "DOTNET_PROCESS_UNAVAILABLE"
        });
    }
});

app.MapPost("/api/v1/executions", async (
    HttpContext httpContext,
    RunnerExecutionRequest request,
    CancellationToken cancellationToken) =>
{
    if (!string.IsNullOrWhiteSpace(options.ApiKey))
    {
        var supplied = httpContext.Request.Headers["X-Runner-Api-Key"].ToString();
        if (!FixedTimeEquals(supplied, options.ApiKey))
            return Results.Unauthorized();
    }

    if (request.ExecutionId == Guid.Empty)
        return Results.BadRequest(new { error = "executionId is required." });

    if (request.Files is null || request.Files.Count == 0)
        return Results.BadRequest(new { error = "At least one project file is required." });

    if (request.TimeoutSeconds <= 0)
        return Results.BadRequest(new { error = "timeoutSeconds must be greater than zero." });

    var timeoutSeconds = Math.Min(
        request.TimeoutSeconds,
        Math.Max(1, options.MaxTimeoutSeconds));

    if (!TryParseAllowedCommand(request.Command, out var arguments))
    {
        return Results.BadRequest(new
        {
            error = "Unsupported command. Allowed: dotnet restore, dotnet build, dotnet test."
        });
    }

    var workspacePath = Path.Combine(
        workspaceRoot,
        request.ExecutionId.ToString("N"));

    try
    {
        MaterializeWorkspace(workspaceRoot, workspacePath, request.Files);
    }
    catch (Exception exception)
    {
        return Results.BadRequest(new
        {
            error = "Workspace materialization failed.",
            detail = exception.Message
        });
    }

    if (string.Equals(request.Command?.Trim(), "dotnet test", StringComparison.OrdinalIgnoreCase))
    {
        var testTarget = ResolveTestTarget(workspacePath);

        if (testTarget is null)
        {
            return Results.Ok(new RunnerExecutionResponse(
                Available: true,
                Success: false,
                ExitCode: null,
                Stdout: string.Empty,
                Stderr: "No solution or test project was found. Generate at least one test project (for example *Tests.csproj) or a solution that includes tests.",
                DurationMs: 0,
                TimedOut: false,
                Reason: "TEST_PROJECT_NOT_FOUND"));
        }

        arguments = ["test", testTarget, "--nologo"];
    }

    var startedAt = Stopwatch.StartNew();
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

    var stdout = new StringBuilder();
    var stderr = new StringBuilder();

    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workspacePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        },
        EnableRaisingEvents = true
    };

    foreach (var argument in arguments)
        process.StartInfo.ArgumentList.Add(argument);

    ConfigureDotnetEnvironment(process.StartInfo, workspacePath);

    process.OutputDataReceived += (_, e) =>
    {
        if (e.Data is not null)
            stdout.AppendLine(e.Data);
    };

    process.ErrorDataReceived += (_, e) =>
    {
        if (e.Data is not null)
            stderr.AppendLine(e.Data);
    };

    try
    {
        if (!process.Start())
        {
            return Results.Ok(new RunnerExecutionResponse(
                Available: false,
                Success: false,
                ExitCode: null,
                Stdout: string.Empty,
                Stderr: "dotnet process could not be started.",
                DurationMs: startedAt.ElapsedMilliseconds,
                TimedOut: false,
                Reason: "RUNNER_PROCESS_START_FAILED"));
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return Results.Ok(new RunnerExecutionResponse(
                Available: true,
                Success: false,
                ExitCode: null,
                Stdout: stdout.ToString(),
                Stderr: stderr.ToString(),
                DurationMs: startedAt.ElapsedMilliseconds,
                TimedOut: true,
                Reason: "EXECUTION_TIMEOUT"));
        }

        return Results.Ok(new RunnerExecutionResponse(
            Available: true,
            Success: process.ExitCode == 0,
            ExitCode: process.ExitCode,
            Stdout: stdout.ToString(),
            Stderr: stderr.ToString(),
            DurationMs: startedAt.ElapsedMilliseconds,
            TimedOut: false,
            Reason: process.ExitCode == 0 ? null : "PROCESS_EXIT_NONZERO"));
    }
    catch (Exception exception)
    {
        return Results.Ok(new RunnerExecutionResponse(
            Available: false,
            Success: false,
            ExitCode: null,
            Stdout: stdout.ToString(),
            Stderr: exception.Message,
            DurationMs: startedAt.ElapsedMilliseconds,
            TimedOut: false,
            Reason: "RUNNER_EXECUTION_ERROR"));
    }
});

app.Run();

static bool TryParseAllowedCommand(
    string? command,
    out string[] arguments)
{
    var normalized = command?.Trim();

    if (string.Equals(normalized, "dotnet restore", StringComparison.OrdinalIgnoreCase))
    {
        arguments = ["restore", "--nologo"];
        return true;
    }

    if (string.Equals(normalized, "dotnet build", StringComparison.OrdinalIgnoreCase))
    {
        arguments = ["build", "--nologo"];
        return true;
    }

    if (string.Equals(normalized, "dotnet test", StringComparison.OrdinalIgnoreCase))
    {
        arguments = ["test", "--nologo"];
        return true;
    }

    arguments = [];
    return false;
}

static void MaterializeWorkspace(
    string rootPath,
    string workspacePath,
    IReadOnlyCollection<RunnerProjectFile> files)
{
    if (Directory.Exists(workspacePath))
        Directory.Delete(workspacePath, recursive: true);

    Directory.CreateDirectory(workspacePath);

    foreach (var file in files)
    {
        if (string.IsNullOrWhiteSpace(file.Path))
            throw new InvalidOperationException("Project file path is required.");

        var normalized = file.Path.Trim().Replace('\\', '/');

        if (normalized.StartsWith('/') ||
            Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(segment => segment == ".."))
        {
            throw new InvalidOperationException(
                $"Unsafe project file path: '{file.Path}'.");
        }

        var destinationPath = Path.GetFullPath(
            Path.Combine(
                workspacePath,
                normalized.Replace('/', Path.DirectorySeparatorChar)));

        var workspacePrefix = workspacePath.EndsWith(Path.DirectorySeparatorChar)
            ? workspacePath
            : workspacePath + Path.DirectorySeparatorChar;

        if (!destinationPath.StartsWith(
                workspacePrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Project path escapes workspace: '{file.Path}'.");
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(destinationPath, file.Content ?? string.Empty);
    }

    var rootPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar)
        ? rootPath
        : rootPath + Path.DirectorySeparatorChar;

    if (!workspacePath.StartsWith(
            rootPrefix,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Workspace escaped runner root.");
    }
}

static string? ResolveTestTarget(string workspacePath)
{
    static bool IsBuildArtifact(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment =>
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, ".profile", StringComparison.OrdinalIgnoreCase));

    var solutions = Directory
        .EnumerateFiles(workspacePath, "*.sln", SearchOption.AllDirectories)
        .Where(path => !IsBuildArtifact(path))
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (solutions.Length > 0)
        return solutions[0];

    var testProjects = Directory
        .EnumerateFiles(workspacePath, "*.csproj", SearchOption.AllDirectories)
        .Where(path => !IsBuildArtifact(path))
        .Where(path =>
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            var directoryName = Path.GetDirectoryName(path) ?? string.Empty;

            return fileName.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
                   directoryName.Contains("Test", StringComparison.OrdinalIgnoreCase);
        })
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    return testProjects.Length == 1
        ? testProjects[0]
        : null;
}

static void ConfigureDotnetEnvironment(
    ProcessStartInfo startInfo,
    string writableRoot)
{
    var profileRoot = Path.Combine(writableRoot, ".profile");
    var dotnetHome = Path.Combine(profileRoot, ".dotnet");
    var nugetPackages = Path.Combine(profileRoot, ".nuget", "packages");
    var appData = Path.Combine(profileRoot, "AppData", "Roaming");
    var localAppData = Path.Combine(profileRoot, "AppData", "Local");
    var temp = Path.Combine(profileRoot, "Temp");

    Directory.CreateDirectory(profileRoot);
    Directory.CreateDirectory(dotnetHome);
    Directory.CreateDirectory(nugetPackages);
    Directory.CreateDirectory(appData);
    Directory.CreateDirectory(localAppData);
    Directory.CreateDirectory(temp);

    startInfo.Environment["DOTNET_CLI_HOME"] = profileRoot;
    startInfo.Environment["NUGET_PACKAGES"] = nugetPackages;
    startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
    startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
    startInfo.Environment["DOTNET_NOLOGO"] = "1";
    startInfo.Environment["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "0";
    startInfo.Environment["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "1";

    startInfo.Environment["USERPROFILE"] = profileRoot;
    startInfo.Environment["HOME"] = profileRoot;
    startInfo.Environment["APPDATA"] = appData;
    startInfo.Environment["LOCALAPPDATA"] = localAppData;
    startInfo.Environment["TEMP"] = temp;
    startInfo.Environment["TMP"] = temp;
}

static bool FixedTimeEquals(string left, string right)
{
    var a = Encoding.UTF8.GetBytes(left ?? string.Empty);
    var b = Encoding.UTF8.GetBytes(right ?? string.Empty);

    return a.Length == b.Length &&
           System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
}

public sealed class RunnerOptions
{
    public string ApiKey { get; init; } = string.Empty;
    public string WorkspaceRootPath { get; init; } = "RunnerData/workspaces";
    public int MaxTimeoutSeconds { get; init; } = 120;
}

public sealed record RunnerExecutionRequest(
    Guid ExecutionId,
    IReadOnlyCollection<RunnerProjectFile> Files,
    string Command,
    int TimeoutSeconds = 60);

public sealed record RunnerProjectFile(
    string Path,
    string Content);

public sealed record RunnerExecutionResponse(
    bool Available,
    bool Success,
    int? ExitCode,
    string Stdout,
    string Stderr,
    long DurationMs,
    bool TimedOut,
    string? Reason);

public partial class Program;
