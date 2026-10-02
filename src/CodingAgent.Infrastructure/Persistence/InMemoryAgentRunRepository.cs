using System.Collections.Concurrent;
using CodingAgent.Application.Abstractions;
using CodingAgent.Domain;

namespace CodingAgent.Infrastructure.Persistence;

public sealed class InMemoryAgentRunRepository : IAgentRunRepository
{
    private readonly ConcurrentDictionary<Guid, AgentRun> _runs = new();

    public Task AddAsync(
        AgentRun run,
        CancellationToken cancellationToken = default)
    {
        if (!_runs.TryAdd(run.ExecutionId, run))
            throw new InvalidOperationException(
                $"Run '{run.ExecutionId}' already exists.");

        return Task.CompletedTask;
    }

    public Task<AgentRun?> GetAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        _runs.TryGetValue(executionId, out var run);
        return Task.FromResult(run);
    }
}
