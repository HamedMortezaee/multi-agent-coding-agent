using CodingAgent.Application.Abstractions;
using CodingAgent.Domain;

namespace CodingAgent.Infrastructure.Workspaces;

public sealed class FileSystemWorkspaceService : IWorkspaceService
{
    private readonly string _rootPath;

    public FileSystemWorkspaceService(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Workspace root path is required.", nameof(rootPath));

        _rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<WorkspaceMaterializationResult> MaterializeAsync(
        Guid executionId,
        IReadOnlyCollection<ProjectFile> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        var workspaceName = executionId.ToString("N");
        var finalPath = Path.Combine(_rootPath, workspaceName);
        var tempPath = Path.Combine(
            _rootPath,
            $".{workspaceName}.{Guid.NewGuid():N}.tmp");

        Directory.CreateDirectory(tempPath);

        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = NormalizeRelativePath(file.Path);
                var destinationPath = GetSafeDestinationPath(tempPath, relativePath);
                var destinationDirectory = Path.GetDirectoryName(destinationPath);

                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);

                await File.WriteAllTextAsync(
                    destinationPath,
                    file.Content ?? string.Empty,
                    cancellationToken);
            }

            if (Directory.Exists(finalPath))
                Directory.Delete(finalPath, recursive: true);

            Directory.Move(tempPath, finalPath);

            return new WorkspaceMaterializationResult(
                Path.Combine("App_Data", "workspaces", workspaceName)
                    .Replace('\\', '/'),
                files.Count);
        }
        catch
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);

            throw;
        }
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Workspace file path is required.");

        var normalized = path.Trim().Replace('\\', '/');

        if (normalized.StartsWith('/') ||
            Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(segment => segment == ".."))
        {
            throw new InvalidOperationException(
                $"Unsafe workspace file path: '{path}'.");
        }

        return normalized;
    }

    private static string GetSafeDestinationPath(
        string workspacePath,
        string relativePath)
    {
        var workspaceFullPath = Path.GetFullPath(workspacePath);
        var destinationPath = Path.GetFullPath(
            Path.Combine(
                workspaceFullPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var prefix = workspaceFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? workspaceFullPath
            : workspaceFullPath + Path.DirectorySeparatorChar;

        if (!destinationPath.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Workspace path escapes the execution directory: '{relativePath}'.");
        }

        return destinationPath;
    }
}
