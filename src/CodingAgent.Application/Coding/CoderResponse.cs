namespace CodingAgent.Application.Coding;

public sealed record CoderResponse(
    string ProjectName,
    IReadOnlyCollection<CoderFile> Files,
    IReadOnlyCollection<string> Notes);

public sealed record CoderFile(
    string Path,
    string Content,
    string Operation);
