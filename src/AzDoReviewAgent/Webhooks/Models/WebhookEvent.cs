namespace AzDoReviewAgent.Webhooks.Models;

/// <summary>
/// Internal DTO that travels through the in-process Channel queue from the
/// webhook endpoint to the review orchestrator.
/// </summary>
public sealed class WebhookEvent
{
    /// <summary>Categorised event type inferred from the raw ADO eventType string.</summary>
    public WebhookEventType EventType { get; init; }

    /// <summary>Numeric ID of the pull request.</summary>
    public int PullRequestId { get; init; }

    /// <summary>GUID of the Git repository.</summary>
    public string RepositoryId { get; init; } = string.Empty;

    /// <summary>Team project name (human-readable).</summary>
    public string ProjectName { get; init; } = string.Empty;

    /// <summary>Raw markdown/plain-text content of the triggering comment.</summary>
    public string CommentContent { get; init; } = string.Empty;

    /// <summary>Identity (GUID or UPN) of the user who posted the comment.</summary>
    public string CommentAuthorId { get; init; } = string.Empty;

    /// <summary>Thread ID the comment belongs to; null when not yet associated.</summary>
    public int? ThreadId { get; init; }

    /// <summary>
    /// File-anchored position context for the thread, or null for PR-level comments.
    /// </summary>
    public ThreadContextDto? ThreadContext { get; init; }

    /// <summary>
    /// Base URL of the Azure DevOps account/collection extracted from
    /// <c>resourceContainers</c> in the webhook envelope.
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;
}

/// <summary>
/// Discriminator for the kind of interaction the agent must handle.
/// </summary>
public enum WebhookEventType
{
    /// <summary>Could not classify the ADO event type.</summary>
    Unknown = 0,

    /// <summary>A /review slash command or @mention requesting a full diff review.</summary>
    ReviewRequested,

    /// <summary>A /ask slash command – the agent should answer a free-form question.</summary>
    ConversationReply
}

/// <summary>
/// Slimmed-down, serialization-agnostic copy of <see cref="AzDoReviewAgent.Webhooks.Models.ThreadContext"/>.
/// Avoids coupling the queue DTO to the webhook deserialization model.
/// </summary>
public sealed class ThreadContextDto
{
    public string FilePath { get; init; } = string.Empty;
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
}
