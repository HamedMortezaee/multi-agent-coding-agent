using System.Text;
using CodingAgent.Application.Abstractions;
using CodingAgent.Domain;

namespace CodingAgent.Application.Reporting;

public sealed record FinalReportResponse(
    Guid ExecutionId,
    string Status,
    DateTimeOffset GeneratedAt,
    string Markdown);

public sealed class FinalReportService(
    IAgentRunRepository repository,
    TimeProvider timeProvider)
{
    public async Task<FinalReportResponse> ExecuteAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var run = await repository.GetAsync(executionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Run '{executionId}' was not found.");

        var markdown = BuildMarkdown(run);

        return new FinalReportResponse(
            run.ExecutionId,
            run.Status.ToString(),
            timeProvider.GetUtcNow(),
            markdown);
    }

    private static string BuildMarkdown(AgentRun run)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# Multi-Agent Coding Agent - Final Report");
        builder.AppendLine();
        builder.AppendLine($"- **Execution ID:** {run.ExecutionId}");
        builder.AppendLine($"- **Status:** {run.Status}");
        builder.AppendLine($"- **Requested By:** {run.RequestedBy}");
        builder.AppendLine($"- **Started At:** {run.StartedAt:O}");
        builder.AppendLine($"- **Deadline:** {run.Deadline:O}");
        builder.AppendLine($"- **Fix Attempts:** {run.FixAttemptCount}");
        builder.AppendLine();

        builder.AppendLine("## User Request");
        builder.AppendLine();
        builder.AppendLine(run.UserRequest);
        builder.AppendLine();

        builder.AppendLine("## Plan");
        builder.AppendLine();

        if (run.Plan is null)
        {
            builder.AppendLine("_No plan was generated._");
        }
        else
        {
            builder.AppendLine($"**Goal:** {run.Plan.Goal}");
            builder.AppendLine();
            builder.AppendLine(run.Plan.Summary);
            builder.AppendLine();

            foreach (var step in run.Plan.Steps.OrderBy(x => x.Order))
                builder.AppendLine($"{step.Order}. **{step.Title}** - {step.Description}");

            builder.AppendLine();

            if (run.Plan.Assumptions.Count > 0)
            {
                builder.AppendLine("### Assumptions");
                foreach (var item in run.Plan.Assumptions)
                    builder.AppendLine($"- {item}");
                builder.AppendLine();
            }

            if (run.Plan.Ambiguities.Count > 0)
            {
                builder.AppendLine("### Ambiguities");
                foreach (var item in run.Plan.Ambiguities)
                    builder.AppendLine($"- {item}");
                builder.AppendLine();
            }
        }

        builder.AppendLine("## Human Review");
        builder.AppendLine();
        builder.AppendLine(string.IsNullOrWhiteSpace(run.HumanFeedback)
            ? "_No human feedback was provided._"
            : run.HumanFeedback);
        builder.AppendLine();

        builder.AppendLine("## Generated Project Files");
        builder.AppendLine();

        if (run.Files.Count == 0)
        {
            builder.AppendLine("_No files were generated._");
        }
        else
        {
            foreach (var file in run.Files.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"### {file.Path}");
                builder.AppendLine();
                builder.AppendLine($"Version: {file.Version}");
                builder.AppendLine();
                builder.AppendLine("~~~~");
                builder.AppendLine(file.Content);
                builder.AppendLine("~~~~");
                builder.AppendLine();
            }
        }

        builder.AppendLine("## Execution Attempts");
        builder.AppendLine();

        if (run.Attempts.Count == 0)
        {
            builder.AppendLine("_No execution attempts were recorded._");
        }
        else
        {
            foreach (var attempt in run.Attempts.OrderBy(x => x.AttemptNumber))
            {
                var result = attempt.ExecutionResult;

                builder.AppendLine($"### Attempt {attempt.AttemptNumber}");
                builder.AppendLine();
                builder.AppendLine($"- **Started:** {attempt.StartedAt:O}");
                builder.AppendLine($"- **Completed:** {attempt.CompletedAt:O}");
                builder.AppendLine($"- **Available:** {result.Available}");
                builder.AppendLine($"- **Success:** {result.Success}");
                builder.AppendLine($"- **Exit Code:** {(result.ExitCode?.ToString() ?? "n/a")}");
                builder.AppendLine($"- **Duration (ms):** {result.DurationMs}");
                builder.AppendLine($"- **Timed Out:** {result.TimedOut}");
                builder.AppendLine($"- **Reason:** {result.Reason ?? "n/a"}");
                builder.AppendLine();

                if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    builder.AppendLine("#### stdout");
                    builder.AppendLine("~~~~text");
                    builder.AppendLine(result.StandardOutput);
                    builder.AppendLine("~~~~");
                    builder.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(result.StandardError))
                {
                    builder.AppendLine("#### stderr");
                    builder.AppendLine("~~~~text");
                    builder.AppendLine(result.StandardError);
                    builder.AppendLine("~~~~");
                    builder.AppendLine();
                }

                if (attempt.Review is not null)
                {
                    builder.AppendLine("#### Reviewer");
                    builder.AppendLine();
                    builder.AppendLine($"- **Success:** {attempt.Review.Success}");
                    builder.AppendLine($"- **Next Action:** {attempt.Review.NextAction}");
                    builder.AppendLine($"- **Summary:** {attempt.Review.Summary}");

                    foreach (var issue in attempt.Review.Issues)
                    {
                        builder.AppendLine(
                            $"- **{issue.Severity} / {issue.Type}:** {issue.Message}" +
                            (string.IsNullOrWhiteSpace(issue.Evidence)
                                ? string.Empty
                                : $" (Evidence: {issue.Evidence})"));
                    }

                    builder.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(attempt.FixSummary))
                {
                    builder.AppendLine("#### Fix Summary");
                    builder.AppendLine();
                    builder.AppendLine(attempt.FixSummary);
                    builder.AppendLine();
                }
            }
        }

        builder.AppendLine("## Final Result");
        builder.AppendLine();
        builder.AppendLine($"The run finished with status **{run.Status}**.");

        var latestReview = run.Attempts
            .OrderByDescending(x => x.AttemptNumber)
            .Select(x => x.Review)
            .FirstOrDefault(x => x is not null);

        if (latestReview is not null)
        {
            builder.AppendLine();
            builder.AppendLine(latestReview.Summary);
        }

        return builder.ToString();
    }
}
