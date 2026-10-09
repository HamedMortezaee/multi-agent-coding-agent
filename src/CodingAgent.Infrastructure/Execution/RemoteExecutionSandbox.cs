using System.Net.Http.Json;
using CodingAgent.Application.Abstractions;
using CodingAgent.Domain;

namespace CodingAgent.Infrastructure.Execution;

public sealed class RemoteExecutionSandbox(
    HttpClient httpClient,
    RemoteRunnerOptions options) : IExecutionSandbox
{
    public async Task<ExecutionResult> ExecuteAsync(
        Guid executionId,
        IReadOnlyCollection<ProjectFile> files,
        string command,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "api/v1/executions");

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            request.Headers.Add("X-Runner-Api-Key", options.ApiKey);

        request.Content = JsonContent.Create(new RunnerExecutionRequest(
            executionId,
            files.Select(file => new RunnerProjectFile(
                file.Path,
                file.Content)).ToArray(),
            command,
            timeoutSeconds));

        HttpResponseMessage response;

        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ExecutionResult(
                Available: false,
                Success: false,
                ExitCode: null,
                StandardOutput: string.Empty,
                StandardError: "Runner request timed out.",
                DurationMs: 0,
                TimedOut: true,
                Reason: "RUNNER_REQUEST_TIMEOUT");
        }
        catch (Exception exception)
        {
            return new ExecutionResult(
                Available: false,
                Success: false,
                ExitCode: null,
                StandardOutput: string.Empty,
                StandardError: exception.Message,
                DurationMs: 0,
                TimedOut: false,
                Reason: "RUNNER_UNREACHABLE");
        }

        using (response)
        {
            RunnerExecutionResponse? payload;

            try
            {
                payload = await response.Content.ReadFromJsonAsync<RunnerExecutionResponse>(
                    cancellationToken: cancellationToken);
            }
            catch
            {
                payload = null;
            }

            if (payload is null)
            {
                return new ExecutionResult(
                    Available: false,
                    Success: false,
                    ExitCode: null,
                    StandardOutput: string.Empty,
                    StandardError: $"Runner returned HTTP {(int)response.StatusCode}.",
                    DurationMs: 0,
                    TimedOut: false,
                    Reason: "RUNNER_INVALID_RESPONSE");
            }

            return new ExecutionResult(
                payload.Available,
                payload.Success,
                payload.ExitCode,
                payload.Stdout ?? string.Empty,
                payload.Stderr ?? string.Empty,
                payload.DurationMs,
                payload.TimedOut,
                payload.Reason);
        }

    }

    private sealed record RunnerExecutionRequest(
        Guid ExecutionId,
        IReadOnlyCollection<RunnerProjectFile> Files,
        string Command,
        int TimeoutSeconds);

    private sealed record RunnerProjectFile(
        string Path,
        string Content);

    private sealed record RunnerExecutionResponse(
        bool Available,
        bool Success,
        int? ExitCode,
        string? Stdout,
        string? Stderr,
        long DurationMs,
        bool TimedOut,
        string? Reason);
}
