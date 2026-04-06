using System.Text.Json;
using AzDoReviewAgent.Configuration;
using AzDoReviewAgent.Processing;
using AzDoReviewAgent.Webhooks.Models;
using Microsoft.Extensions.Options;

namespace AzDoReviewAgent.Webhooks;

/// <summary>
/// Registers the Azure DevOps webhook ingestion endpoint and wires in the
/// <see cref="WebhookAuthMiddleware"/> for the <c>/api/webhooks/</c> path prefix.
/// </summary>
public static class WebhookEndpoints
{
    private const string AdoPrCommentEventType = "ms.vss-code.git-pullrequest-comment-event";
    private const string SupportedResourceVersion = "2.0";

    public static WebApplication MapWebhookEndpoints(this WebApplication app)
    {
        // Protect the entire /api/webhooks/* subtree with Basic auth.
        app.UseMiddleware<WebhookAuthMiddleware>();

        app.MapPost("/api/webhooks/azuredevops", HandleWebhookAsync)
           .WithName("AzureDevOpsWebhook")
           .Accepts<WebhookPayload>("application/json");

        return app;
    }

    // ── Handler ───────────────────────────────────────────────────────────────

    private static async Task<IResult> HandleWebhookAsync(
        HttpContext httpContext,
        WebhookPayload payload,
        TriggerDetector triggerDetector,
        IWebhookQueue queue,
        IOptions<AzureDevOpsOptions> azureDevOpsOptions,
        ILogger<WebhookAuthMiddleware> logger, // reuse category; keeps DI simple
        CancellationToken ct)
    {
        // 1. Validate resource version
        if (!string.Equals(payload.ResourceVersion, SupportedResourceVersion, StringComparison.Ordinal))
        {
            logger.LogWarning("Rejected webhook with unsupported resourceVersion={ResourceVersion}", payload.ResourceVersion);
            return Results.BadRequest(new { error = $"Unsupported resourceVersion '{payload.ResourceVersion}'. Expected '{SupportedResourceVersion}'." });
        }

        // 2. Only handle PR comment events; silently discard everything else
        if (!string.Equals(payload.EventType, AdoPrCommentEventType, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("Ignoring unhandled event type {EventType}", payload.EventType);
            return Results.NoContent();
        }

        // 3. Deserialise the typed resource
        CommentResource? resource;
        try
        {
            resource = payload.Resource.Deserialize<CommentResource>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialise CommentResource from webhook payload");
            return Results.BadRequest(new { error = "Malformed resource payload." });
        }

        if (resource?.Comment is null || resource.PullRequest is null)
        {
            logger.LogWarning("Webhook payload is missing required resource fields");
            return Results.BadRequest(new { error = "Missing required resource fields (comment or pullRequest)." });
        }

        // 4. Self-comment guard – drop events from the agent's own account
        var agentOptions = azureDevOpsOptions.Value;
        var authorId       = resource.Comment.Author?.Id ?? string.Empty;
        var authorUnique   = resource.Comment.Author?.UniqueName ?? string.Empty;

        if (string.Equals(authorId, agentOptions.AgentUserId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authorUnique, agentOptions.AgentUserId, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("Skipping self-comment from agent (authorId={AuthorId})", authorId);
            return Results.NoContent();
        }

        // 5. Detect trigger
        var triggerResult = triggerDetector.DetectTrigger(resource.Comment.Content);
        if (!triggerResult.IsTrigger)
        {
            logger.LogDebug("Comment did not match any trigger pattern; ignoring");
            return Results.NoContent();
        }

        // 6. Map to internal event type
        var eventType = triggerResult.TriggerType switch
        {
            TriggerType.Review  => WebhookEventType.ReviewRequested,
            TriggerType.Ask     => WebhookEventType.ConversationReply,
            TriggerType.Mention => WebhookEventType.ReviewRequested,
            _                   => WebhookEventType.Unknown
        };

        if (eventType == WebhookEventType.Unknown)
            return Results.NoContent();

        // 7. Extract base URL from resourceContainers (account → collection → fallback to config)
        var baseUrl = payload.ResourceContainers?.Account?.BaseUrl
                   ?? payload.ResourceContainers?.Collection?.BaseUrl
                   ?? agentOptions.ServerUrl;

        // 8. Build thread context DTO
        ThreadContextDto? threadContext = null;
        if (resource.Thread?.ThreadContext is { } tc && !string.IsNullOrWhiteSpace(tc.FilePath))
        {
            threadContext = new ThreadContextDto
            {
                FilePath  = tc.FilePath,
                StartLine = tc.RightFileStart?.Line,
                EndLine   = tc.RightFileEnd?.Line
            };
        }

        // 9. Build and enqueue the internal event
        var webhookEvent = new WebhookEvent
        {
            EventType        = eventType,
            PullRequestId    = resource.PullRequest.PullRequestId,
            RepositoryId     = resource.PullRequest.Repository?.Id ?? string.Empty,
            ProjectName      = resource.PullRequest.Repository?.Project?.Name ?? string.Empty,
            CommentContent   = resource.Comment.Content,
            CommentAuthorId  = authorId,
            ThreadId         = resource.Thread?.Id,
            ThreadContext    = threadContext,
            BaseUrl          = baseUrl.TrimEnd('/')
        };

        await queue.EnqueueAsync(webhookEvent, ct);

        logger.LogInformation(
            "Enqueued {EventType} event for PR #{PullRequestId} in project {ProjectName}",
            eventType, webhookEvent.PullRequestId, webhookEvent.ProjectName);

        return Results.Accepted();
    }
}
