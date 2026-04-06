using System.Text.Json.Serialization;

namespace AzDoReviewAgent.AzureDevOps.Models;

public sealed class PullRequest
{
    [JsonPropertyName("pullRequestId")]
    public int PullRequestId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("sourceRefName")]
    public string SourceRefName { get; set; } = string.Empty;

    [JsonPropertyName("targetRefName")]
    public string TargetRefName { get; set; } = string.Empty;

    [JsonPropertyName("isDraft")]
    public bool IsDraft { get; set; }

    [JsonPropertyName("createdBy")]
    public IdentityRef CreatedBy { get; set; } = new();

    [JsonPropertyName("lastMergeSourceCommit")]
    public GitCommitRef? LastMergeSourceCommit { get; set; }

    [JsonPropertyName("lastMergeTargetCommit")]
    public GitCommitRef? LastMergeTargetCommit { get; set; }

    [JsonPropertyName("repository")]
    public GitRepository Repository { get; set; } = new();
}

public sealed class IdentityRef
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("uniqueName")]
    public string UniqueName { get; set; } = string.Empty;
}

public sealed class GitCommitRef
{
    [JsonPropertyName("commitId")]
    public string CommitId { get; set; } = string.Empty;
}

public sealed class GitRepository
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("project")]
    public TeamProjectReference Project { get; set; } = new();
}

public sealed class TeamProjectReference
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
