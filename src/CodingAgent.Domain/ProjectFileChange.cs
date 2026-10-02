namespace CodingAgent.Domain;

public sealed record ProjectFileChange(
    string Path,
    string Operation,
    string Content,
    string Reason);
