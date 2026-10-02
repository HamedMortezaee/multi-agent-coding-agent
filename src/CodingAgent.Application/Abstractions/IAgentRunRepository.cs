using CodingAgent.Domain;

namespace CodingAgent.Application.Abstractions;

public interface IAgentRunRepository
{
    Task AddAsync(AgentRun run, CancellationToken cancellationToken = default);
    Task<AgentRun?> GetAsync(Guid executionId, CancellationToken cancellationToken = default);
    Task SaveAsync(AgentRun run, CancellationToken cancellationToken = default);
}
