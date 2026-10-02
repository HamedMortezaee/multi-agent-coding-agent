namespace CodingAgent.Api.Contracts;

public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    IReadOnlyCollection<ApiError> Errors,
    ApiMeta Meta);

public sealed record ApiError(
    string Code,
    string Message,
    string? Details = null,
    bool Retryable = false);

public sealed record ApiMeta(
    Guid? ExecutionId,
    DateTimeOffset Timestamp);
