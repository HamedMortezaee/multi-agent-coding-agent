namespace CodingAgent.Domain;

public sealed class ProjectFile
{
    public string Path { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public int Version { get; init; } = 1;
}
