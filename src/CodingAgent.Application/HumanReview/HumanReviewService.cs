using CodingAgent.Application.Abstractions;

namespace CodingAgent.Application.HumanReview;

public sealed class HumanReviewService(
    IAgentRunRepository repository)
{
    public async Task<HumanReviewResponse> ExecuteAsync(
        Guid executionId,
        HumanReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var run = await repository.GetAsync(executionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Run '{executionId}' was not found.");

        var decision = request.Decision?.Trim().ToLowerInvariant();

        switch (decision)
        {
            case "approve":
                run.ApprovePlan(request.Feedback);
                break;

            case "modify":
                if (string.IsNullOrWhiteSpace(request.Feedback))
                    throw new ArgumentException(
                        "Feedback is required when decision is 'modify'.",
                        nameof(request));

                run.RequestPlanModification(request.Feedback);
                break;

            case "reject":
                run.RejectPlan(request.Feedback);
                break;

            default:
                throw new ArgumentException(
                    "Decision must be one of: approve, modify, reject.",
                    nameof(request));
        }

        return new HumanReviewResponse(
            run.ExecutionId,
            decision!,
            run.Status.ToString(),
            run.HumanFeedback);
    }
}
