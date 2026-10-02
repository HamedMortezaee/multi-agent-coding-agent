using CodingAgent.Application.Abstractions;
using CodingAgent.Domain;

namespace CodingAgent.Application.Runs.CreateRun;

public sealed class CreateRunService(
    IAgentRunRepository repository,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan MaxRunDuration = TimeSpan.FromMinutes(15);

    public async Task<CreateRunResponse> ExecuteAsync(
        CreateRunRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Request))
            throw new ArgumentException("User request is required.", nameof(request));

        var now = timeProvider.GetUtcNow();

        var run = AgentRun.Create(
            request.Request,
            request.RequestedBy ?? "user",
            now,
            MaxRunDuration);

        await repository.AddAsync(run, cancellationToken);

        return new CreateRunResponse(
            run.ExecutionId,
            run.Status.ToString(),
            run.StartedAt,
            run.Deadline);
    }
}
