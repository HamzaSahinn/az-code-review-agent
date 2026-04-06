using System.Text.Json;
using System.Text.Json.Serialization;

namespace AzDoReviewAgent.Webhooks.Models;

/// <summary>
/// Root payload envelope for Azure DevOps service hook events.
/// </summary>
public sealed class WebhookPayload
{
    /// <summary>
    /// The event type identifier, e.g. "ms.vss-code.git-pullrequest-comment-event".
    /// </summary>
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// The version of the resource schema included in this payload.
    /// </summary>
    [JsonPropertyName("resourceVersion")]
    public string ResourceVersion { get; set; } = string.Empty;

    /// <summary>
    /// Unique identifier of the service hooks subscription that triggered this event.
    /// </summary>
    [JsonPropertyName("subscriptionId")]
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>
    /// Sequential notification ID for this delivery attempt.
    /// </summary>
    [JsonPropertyName("notificationId")]
    public int NotificationId { get; set; }

    /// <summary>
    /// The raw JSON resource object; deserialized into a concrete type by the endpoint handler.
    /// </summary>
    [JsonPropertyName("resource")]
    public JsonElement Resource { get; set; }

    /// <summary>
    /// Container references (account, project, collection) embedded in every ADO webhook.
    /// Provides the base URL required to call back into Azure DevOps.
    /// </summary>
    [JsonPropertyName("resourceContainers")]
    public ResourceContainers? ResourceContainers { get; set; }
}

/// <summary>
/// Container links present in every Azure DevOps service hook payload.
/// </summary>
public sealed class ResourceContainers
{
    [JsonPropertyName("account")]
    public ResourceContainer? Account { get; set; }

    [JsonPropertyName("collection")]
    public ResourceContainer? Collection { get; set; }

    [JsonPropertyName("project")]
    public ResourceContainer? Project { get; set; }
}

/// <summary>
/// A single resource container reference with a resolvable base URL.
/// </summary>
public sealed class ResourceContainer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = string.Empty;
}
