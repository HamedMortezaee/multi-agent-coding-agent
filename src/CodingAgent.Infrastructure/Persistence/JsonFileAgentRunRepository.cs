using System.Collections.Concurrent;
using System.Text.Json;
using CodingAgent.Application.Abstractions;
using CodingAgent.Domain;

namespace CodingAgent.Infrastructure.Persistence;

public sealed class JsonFileAgentRunRepository : IAgentRunRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _rootPath;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public JsonFileAgentRunRepository(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Persistence root path is required.", nameof(rootPath));

        _rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task AddAsync(
        AgentRun run,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(run.ExecutionId);
        await gate.WaitAsync(cancellationToken);

        try
        {
            var path = GetPath(run.ExecutionId);

            if (File.Exists(path))
                throw new InvalidOperationException(
                    $"Run '{run.ExecutionId}' already exists.");

            await WriteAsync(path, run, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AgentRun?> GetAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(executionId);
        await gate.WaitAsync(cancellationToken);

        try
        {
            var path = GetPath(executionId);

            if (!File.Exists(path))
                return null;

            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<AgentRunSnapshot>(
                stream,
                JsonOptions,
                cancellationToken);

            return snapshot?.ToDomain();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(
        AgentRun run,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(run.ExecutionId);
        await gate.WaitAsync(cancellationToken);

        try
        {
            var path = GetPath(run.ExecutionId);

            if (!File.Exists(path))
                throw new KeyNotFoundException(
                    $"Run '{run.ExecutionId}' was not found.");

            await WriteAsync(path, run, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim GetLock(Guid executionId) =>
        _locks.GetOrAdd(executionId, _ => new SemaphoreSlim(1, 1));

    private string GetPath(Guid executionId) =>
        Path.Combine(_rootPath, $"{executionId:N}.json");

    private static async Task WriteAsync(
        string path,
        AgentRun run,
        CancellationToken cancellationToken)
    {
        var snapshot = AgentRunSnapshot.FromDomain(run);
        var tempPath = path + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                snapshot,
                JsonOptions,
                cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
