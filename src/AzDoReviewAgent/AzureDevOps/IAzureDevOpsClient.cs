using AzDoReviewAgent.AzureDevOps.Models;

namespace AzDoReviewAgent.AzureDevOps;

public interface IAzureDevOpsClient
{
    Task<PullRequest> GetPullRequestAsync(
        string project,
        string repositoryId,
        int pullRequestId,
        CancellationToken ct = default);

    Task<List<PullRequestIteration>> GetIterationsAsync(
        string project,
        string repositoryId,
        int pullRequestId,
        CancellationToken ct = default);

    Task<List<PullRequestChange>> GetIterationChangesAsync(
        string project,
        string repositoryId,
        int pullRequestId,
        int iterationId,
        CancellationToken ct = default);

    /// <summary>Returns null for binary files.</summary>
    Task<string?> GetFileContentAsync(
        string project,
        string repositoryId,
        string path,
        string commitId,
        CancellationToken ct = default);

    Task<List<CommentThread>> GetThreadsAsync(
        string project,
        string repositoryId,
        int pullRequestId,
        CancellationToken ct = default);

    Task CreateThreadAsync(
        string project,
        string repositoryId,
        int pullRequestId,
        NewThread thread,
        CancellationToken ct = default);

    Task ReplyToThreadAsync(
        string project,
        string repositoryId,
        int pullRequestId,
        int threadId,
        string content,
        CancellationToken ct = default);

    Task<bool> ValidateConnectionAsync();
}
