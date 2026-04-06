using AzDoReviewAgent.AzureDevOps;
using AzDoReviewAgent.AzureDevOps.Models;
using AzDoReviewAgent.Configuration;
using AzDoReviewAgent.Diff;
using AzDoReviewAgent.Review;
using AzDoReviewAgent.Webhooks.Models;
using Microsoft.Extensions.Options;
using AdoCommentThread = AzDoReviewAgent.AzureDevOps.Models.CommentThread;
using AdoThreadContext = AzDoReviewAgent.AzureDevOps.Models.ThreadContext;

namespace AzDoReviewAgent.Processing;

/// <summary>
/// Long-running background service that consumes <see cref="WebhookEvent"/>s
/// from <see cref="IWebhookQueue"/> and orchestrates pull-request reviews and
/// conversation replies against the Azure DevOps REST API.
/// </summary>
public sealed class ReviewOrchestrator : BackgroundService
{
    private readonly IWebhookQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReviewOrchestrator> _logger;

    public ReviewOrchestrator(
        IWebhookQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ReviewOrchestrator> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReviewOrchestrator started – waiting for events");

        while (!stoppingToken.IsCancellationRequested)
        {
            WebhookEvent evt;
            try
            {
                evt = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                await ProcessEventAsync(evt, scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unhandled error processing {EventType} for PR {PrId} in {Project}/{Repo}",
                    evt.EventType, evt.PullRequestId, evt.ProjectName, evt.RepositoryId);
            }
        }

        _logger.LogInformation("ReviewOrchestrator stopping");
    }

    // ── Event dispatch ──────────────────────────────────────────────────────

    private Task ProcessEventAsync(
        WebhookEvent evt,
        IServiceProvider sp,
        CancellationToken ct)
    {
        return evt.EventType switch
        {
            WebhookEventType.ReviewRequested => HandleReviewAsync(evt, sp, ct),
            WebhookEventType.ConversationReply => HandleConversationAsync(evt, sp, ct),
            _ => LogUnknownEvent(evt)
        };
    }

    private Task LogUnknownEvent(WebhookEvent evt)
    {
        _logger.LogWarning("Ignoring unknown event type {EventType} for PR {PrId}",
            evt.EventType, evt.PullRequestId);
        return Task.CompletedTask;
    }

    // ── Full review pipeline ────────────────────────────────────────────────

    private async Task HandleReviewAsync(
        WebhookEvent evt,
        IServiceProvider sp,
        CancellationToken ct)
    {
        var devOps = sp.GetRequiredService<IAzureDevOpsClient>();
        var diffService = sp.GetRequiredService<IDiffService>();
        var reviewService = sp.GetRequiredService<IReviewService>();
        var reviewOptions = sp.GetRequiredService<IOptions<ReviewOptions>>().Value;
        var azDoOptions = sp.GetRequiredService<IOptions<AzureDevOpsOptions>>().Value;

        _logger.LogInformation(
            "Starting review for PR {PrId} in {Project}/{Repo}",
            evt.PullRequestId, evt.ProjectName, evt.RepositoryId);

        // 1. Fetch pull request metadata
        var pr = await devOps.GetPullRequestAsync(
            evt.ProjectName, evt.RepositoryId, evt.PullRequestId, ct);

        if (pr.Status != "active")
        {
            _logger.LogInformation("PR {PrId} status is '{Status}' – skipping review",
                evt.PullRequestId, pr.Status);
            return;
        }

        // 2. Fetch iterations to find the latest one
        var iterations = await devOps.GetIterationsAsync(
            evt.ProjectName, evt.RepositoryId, evt.PullRequestId, ct);

        if (iterations.Count == 0)
        {
            _logger.LogWarning("PR {PrId} has no iterations – skipping", evt.PullRequestId);
            return;
        }

        var latestIteration = iterations[^1];
        var iterationId = latestIteration.Id;

        // 3. Fetch changes in the latest iteration
        var changes = await devOps.GetIterationChangesAsync(
            evt.ProjectName, evt.RepositoryId, evt.PullRequestId, iterationId, ct);

        // 4. Filter to reviewable files
        var filesToReview = FilterChanges(changes, reviewOptions);

        if (filesToReview.Count == 0)
        {
            _logger.LogInformation("No reviewable files in PR {PrId}", evt.PullRequestId);
            await PostSummaryComment(devOps, evt,
                "🔍 **Code Review Agent** — No reviewable files found in this PR.", ct);
            return;
        }

        // 5. Need source and target commit IDs for file content retrieval
        var sourceCommitId = pr.LastMergeSourceCommit?.CommitId;
        var targetCommitId = pr.LastMergeTargetCommit?.CommitId;

        if (string.IsNullOrEmpty(sourceCommitId))
        {
            _logger.LogWarning("PR {PrId} has no merge source commit – skipping", evt.PullRequestId);
            return;
        }

        // 6. Review each file
        var allFindings = new List<ReviewFinding>();
        var filesReviewed = 0;

        foreach (var change in filesToReview)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var findings = await ReviewSingleFileAsync(
                    devOps, diffService, reviewService,
                    evt, pr, change,
                    sourceCommitId, targetCommitId,
                    iterationId, ct);

                allFindings.AddRange(findings);
                filesReviewed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reviewing file {FilePath} in PR {PrId}",
                    change.Item.Path, evt.PullRequestId);
            }
        }

        _logger.LogInformation(
            "Reviewed {FileCount} files in PR {PrId} – found {FindingCount} issues",
            filesReviewed, evt.PullRequestId, allFindings.Count);

        // 7. Post line-level comments for each finding
        foreach (var finding in allFindings)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var changeTrackingId = filesToReview
                    .FirstOrDefault(c => c.Item.Path.Equals(finding.FilePath, StringComparison.OrdinalIgnoreCase))
                    ?.ChangeTrackingId;

                await PostFindingCommentAsync(
                    devOps, evt, finding, iterationId, changeTrackingId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error posting comment for finding at {FilePath}:{StartLine}",
                    finding.FilePath, finding.StartLine);
            }
        }

        // 8. Post PR-level summary
        try
        {
            var summary = await reviewService.GenerateSummaryAsync(
                pr.Title, pr.Description ?? string.Empty, allFindings, ct);

            var summaryMarkdown = FormatSummary(summary, filesReviewed, filesToReview.Count);
            await PostSummaryComment(devOps, evt, summaryMarkdown, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error posting summary for PR {PrId}", evt.PullRequestId);
        }
    }

    private async Task<List<ReviewFinding>> ReviewSingleFileAsync(
        IAzureDevOpsClient devOps,
        IDiffService diffService,
        IReviewService reviewService,
        WebhookEvent evt,
        PullRequest pr,
        PullRequestChange change,
        string sourceCommitId,
        string? targetCommitId,
        int iterationId,
        CancellationToken ct)
    {
        var filePath = change.Item.Path;
        var changeType = change.ChangeType.ToLowerInvariant();

        _logger.LogDebug("Reviewing {FilePath} ({ChangeType})", filePath, changeType);

        // Get head (source branch) content — always needed
        string? headContent = await devOps.GetFileContentAsync(
            evt.ProjectName, evt.RepositoryId, filePath, sourceCommitId, ct);

        if (headContent is null)
        {
            _logger.LogDebug("Skipping binary file {FilePath}", filePath);
            return [];
        }

        // Get base (target branch) content — needed for edits, not for adds
        string? baseContent = null;
        if (!changeType.Equals("add", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(targetCommitId))
        {
            try
            {
                baseContent = await devOps.GetFileContentAsync(
                    evt.ProjectName, evt.RepositoryId, filePath, targetCommitId, ct);
            }
            catch (HttpRequestException)
            {
                // File may not exist in target branch (renamed etc.) — treat as add
                _logger.LogDebug(
                    "Could not fetch base content for {FilePath} – treating as new file", filePath);
            }
        }

        // Compute diff
        var diffResult = diffService.ComputeDiff(baseContent, headContent, filePath, changeType);

        if (diffResult.FileChanges.Count == 0)
            return [];

        var fileChange = diffResult.FileChanges[0];
        var formattedDiff = diffService.FormatDiffForReview(fileChange, headContent);

        // AI review
        var findings = await reviewService.ReviewFileAsync(
            filePath, formattedDiff, headContent,
            pr.Title, pr.Description ?? string.Empty, ct);

        return findings;
    }

    // ── Conversation reply pipeline ─────────────────────────────────────────

    private async Task HandleConversationAsync(
        WebhookEvent evt,
        IServiceProvider sp,
        CancellationToken ct)
    {
        if (!evt.ThreadId.HasValue)
        {
            _logger.LogWarning(
                "ConversationReply event for PR {PrId} has no ThreadId – skipping",
                evt.PullRequestId);
            return;
        }

        var devOps = sp.GetRequiredService<IAzureDevOpsClient>();
        var reviewService = sp.GetRequiredService<IReviewService>();
        var azDoOptions = sp.GetRequiredService<IOptions<AzureDevOpsOptions>>().Value;

        _logger.LogInformation(
            "Handling conversation reply for PR {PrId}, thread {ThreadId}",
            evt.PullRequestId, evt.ThreadId.Value);

        // 1. Fetch all threads to find the target thread
        var threads = await devOps.GetThreadsAsync(
            evt.ProjectName, evt.RepositoryId, evt.PullRequestId, ct);

        var thread = threads.FirstOrDefault(t => t.Id == evt.ThreadId.Value);
        if (thread is null)
        {
            _logger.LogWarning("Thread {ThreadId} not found in PR {PrId}",
                evt.ThreadId.Value, evt.PullRequestId);
            return;
        }

        // 2. Build conversation history (excluding the agent's own identity)
        var conversationHistory = thread.Comments
            .OrderBy(c => c.Id)
            .Where(c => !c.Author.Id.Equals(azDoOptions.AgentUserId, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Content)
            .ToList();

        // 3. Get file context if thread is anchored to a file
        var fileSnippet = string.Empty;
        if (thread.ThreadContext is not null
            && !string.IsNullOrWhiteSpace(thread.ThreadContext.FilePath))
        {
            fileSnippet = await GetFileSnippetForThread(devOps, evt, thread.ThreadContext, ct);
        }

        // 4. Build thread context description
        var threadContextDescription = BuildThreadContextDescription(thread);

        // 5. AI-generated reply
        var reply = await reviewService.GenerateReplyAsync(
            threadContextDescription, fileSnippet, conversationHistory, ct);

        // 6. Post reply
        await devOps.ReplyToThreadAsync(
            evt.ProjectName, evt.RepositoryId, evt.PullRequestId,
            evt.ThreadId.Value, reply, ct);

        _logger.LogInformation(
            "Posted conversation reply to thread {ThreadId} in PR {PrId}",
            evt.ThreadId.Value, evt.PullRequestId);
    }

    private async Task<string> GetFileSnippetForThread(
        IAzureDevOpsClient devOps,
        WebhookEvent evt,
        AdoThreadContext threadContext,
        CancellationToken ct)
    {
        try
        {
            // Get the PR to find the source commit
            var pr = await devOps.GetPullRequestAsync(
                evt.ProjectName, evt.RepositoryId, evt.PullRequestId, ct);

            var commitId = pr.LastMergeSourceCommit?.CommitId;
            if (string.IsNullOrEmpty(commitId))
                return string.Empty;

            var content = await devOps.GetFileContentAsync(
                evt.ProjectName, evt.RepositoryId, threadContext.FilePath, commitId, ct);

            if (content is null)
                return string.Empty;

            // Extract relevant lines around the thread context
            var lines = content.Split('\n');
            var startLine = threadContext.RightFileStart?.Line ?? 1;
            var endLine = threadContext.RightFileEnd?.Line ?? startLine;

            // Add surrounding context (10 lines before and after)
            const int contextPadding = 10;
            var snippetStart = Math.Max(0, startLine - 1 - contextPadding);
            var snippetEnd = Math.Min(lines.Length, endLine + contextPadding);

            var snippetLines = lines[snippetStart..snippetEnd]
                .Select((line, i) => $"{snippetStart + i + 1}: {line.TrimEnd('\r')}");

            return $"File: {threadContext.FilePath}\n{string.Join('\n', snippetLines)}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not fetch file snippet for thread context at {FilePath}",
                threadContext.FilePath);
            return string.Empty;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Filters iteration changes to only include reviewable files based on
    /// configured extensions, exclude patterns, and size limits.
    /// </summary>
    private static List<PullRequestChange> FilterChanges(
        List<PullRequestChange> changes,
        ReviewOptions options)
    {
        return changes
            .Where(c =>
            {
                var path = c.Item.Path;

                // Must have a file path
                if (string.IsNullOrWhiteSpace(path))
                    return false;

                // Skip deletions — nothing to review
                if (c.ChangeType.Equals("delete", StringComparison.OrdinalIgnoreCase))
                    return false;

                // Check file extension whitelist
                var ext = Path.GetExtension(path);
                if (!options.FileExtensions.Any(
                    e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                    return false;

                // Check exclude patterns
                if (options.ExcludePatterns.Any(pattern => MatchesGlob(path, pattern)))
                    return false;

                return true;
            })
            .Take(options.MaxFilesPerReview)
            .ToList();
    }

    /// <summary>
    /// Simple glob matching supporting * (single segment) and ** (any depth).
    /// </summary>
    private static bool MatchesGlob(string path, string pattern)
    {
        // Convert glob to regex: ** → .*, * → [^/]*, ? → .
        var regexPattern = "^" +
            System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace(@"\*\*", ".*")
                .Replace(@"\*", @"[^/]*")
                .Replace(@"\?", ".") +
            "$";

        return System.Text.RegularExpressions.Regex.IsMatch(
            path, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private async Task PostFindingCommentAsync(
        IAzureDevOpsClient devOps,
        WebhookEvent evt,
        ReviewFinding finding,
        int iterationId,
        int? changeTrackingId,
        CancellationToken ct)
    {
        var severityEmoji = finding.Severity.ToLowerInvariant() switch
        {
            "critical" => "🔴",
            "warning" => "🟡",
            "suggestion" => "💡",
            "praise" => "✅",
            _ => "ℹ️"
        };

        var content = $"{severityEmoji} **{finding.Title}**\n\n{finding.Message}";

        if (!string.IsNullOrWhiteSpace(finding.Suggestion))
        {
            content += $"\n\n**Suggestion:**\n```\n{finding.Suggestion}\n```";
        }

        var thread = new NewThread
        {
            Content = content,
            FilePath = finding.FilePath,
            StartLine = finding.StartLine,
            EndLine = finding.EndLine > finding.StartLine ? finding.EndLine : null,
            ChangeTrackingId = changeTrackingId,
            IterationId = iterationId,
            Status = 1 // Active
        };

        await devOps.CreateThreadAsync(
            evt.ProjectName, evt.RepositoryId, evt.PullRequestId, thread, ct);
    }

    private static Task PostSummaryComment(
        IAzureDevOpsClient devOps,
        WebhookEvent evt,
        string content,
        CancellationToken ct)
    {
        var thread = new NewThread
        {
            Content = content,
            Status = 1 // Active, PR-level (no file context)
        };

        return devOps.CreateThreadAsync(
            evt.ProjectName, evt.RepositoryId, evt.PullRequestId, thread, ct);
    }

    private static string FormatSummary(ReviewSummary summary, int filesReviewed, int totalFiles)
    {
        var scoreEmoji = summary.Score switch
        {
            >= 90 => "🟢",
            >= 70 => "🟡",
            >= 50 => "🟠",
            _ => "🔴"
        };

        return $"""
            ## {scoreEmoji} Code Review Summary — Score: {summary.Score}/100

            **Files reviewed:** {filesReviewed}/{totalFiles} | **Total findings:** {summary.TotalFindings}

            | 🔴 Critical | 🟡 Warning | 💡 Suggestion |
            |:-----------:|:----------:|:-------------:|
            | {summary.CriticalCount} | {summary.WarningCount} | {summary.SuggestionCount} |

            ### Assessment

            {summary.OverallAssessment}

            {summary.Summary}

            ---
            *Generated by Code Review Agent*
            """;
    }

    private static string BuildThreadContextDescription(AdoCommentThread thread)
    {
        var parts = new List<string>();

        if (thread.ThreadContext is not null)
        {
            parts.Add($"File: {thread.ThreadContext.FilePath}");

            if (thread.ThreadContext.RightFileStart is not null)
            {
                var start = thread.ThreadContext.RightFileStart.Line;
                var end = thread.ThreadContext.RightFileEnd?.Line ?? start;
                parts.Add(start == end
                    ? $"Line: {start}"
                    : $"Lines: {start}-{end}");
            }
        }

        parts.Add($"Thread status: {thread.Status}");

        return string.Join(" | ", parts);
    }
}
