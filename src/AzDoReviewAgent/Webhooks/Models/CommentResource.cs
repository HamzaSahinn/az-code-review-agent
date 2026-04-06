using System.Text.Json.Serialization;

namespace AzDoReviewAgent.Webhooks.Models;

/// <summary>
/// Typed resource payload for <c>ms.vss-code.git-pullrequest-comment-event</c>.
/// </summary>
public sealed class CommentResource
{
    [JsonPropertyName("comment")]
    public PullRequestComment? Comment { get; set; }

    [JsonPropertyName("pullRequest")]
    public PullRequestInfo? PullRequest { get; set; }

    [JsonPropertyName("thread")]
    public CommentThread? Thread { get; set; }
}

// ── Comment ──────────────────────────────────────────────────────────────────

/// <summary>A single comment posted on a pull request.</summary>
public sealed class PullRequestComment
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public CommentAuthor? Author { get; set; }

    [JsonPropertyName("publishedDate")]
    public DateTimeOffset PublishedDate { get; set; }

    /// <summary>
    /// Comment type: 1 = text, 2 = codeChange, 3 = system, etc.
    /// </summary>
    [JsonPropertyName("commentType")]
    public int CommentType { get; set; }

    [JsonPropertyName("parentCommentId")]
    public int? ParentCommentId { get; set; }
}

/// <summary>Identity information for the comment author.</summary>
public sealed class CommentAuthor
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("uniqueName")]
    public string UniqueName { get; set; } = string.Empty;
}

// ── Pull Request ──────────────────────────────────────────────────────────────

/// <summary>Summary of the pull request the comment belongs to.</summary>
public sealed class PullRequestInfo
{
    [JsonPropertyName("pullRequestId")]
    public int PullRequestId { get; set; }

    [JsonPropertyName("repository")]
    public RepositoryInfo? Repository { get; set; }
}

/// <summary>Repository reference embedded in a PR payload.</summary>
public sealed class RepositoryInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("webUrl")]
    public string WebUrl { get; set; } = string.Empty;

    [JsonPropertyName("project")]
    public ProjectInfo? Project { get; set; }
}

/// <summary>Team project reference.</summary>
public sealed class ProjectInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

// ── Thread ────────────────────────────────────────────────────────────────────

/// <summary>The comment thread the new comment was posted to.</summary>
public sealed class CommentThread
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// Present only for threads that are anchored to a specific file location.
    /// </summary>
    [JsonPropertyName("threadContext")]
    public ThreadContext? ThreadContext { get; set; }
}

/// <summary>File and line-range context for a code-comment thread.</summary>
public sealed class ThreadContext
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("rightFileStart")]
    public FilePosition? RightFileStart { get; set; }

    [JsonPropertyName("rightFileEnd")]
    public FilePosition? RightFileEnd { get; set; }
}

/// <summary>A line + offset position within a file diff.</summary>
public sealed class FilePosition
{
    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }
}
