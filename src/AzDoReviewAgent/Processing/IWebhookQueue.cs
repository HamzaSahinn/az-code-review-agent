using AzDoReviewAgent.Webhooks.Models;

namespace AzDoReviewAgent.Processing;

/// <summary>
/// In-process queue that decouples webhook ingestion from review processing.
/// </summary>
public interface IWebhookQueue
{
    /// <summary>
    /// Adds a <see cref="WebhookEvent"/> to the tail of the queue.
    /// </summary>
    /// <param name="webhookEvent">The event to enqueue.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// When the bounded channel is full the call will back-pressure (wait)
    /// until space is available, honouring <paramref name="ct"/>.
    /// </remarks>
    ValueTask EnqueueAsync(WebhookEvent webhookEvent, CancellationToken ct = default);

    /// <summary>
    /// Removes and returns the next <see cref="WebhookEvent"/> from the queue.
    /// Blocks asynchronously until an item is available or the token is cancelled.
    /// </summary>
    /// <param name="ct">Cancellation token – typically the application lifetime token.</param>
    ValueTask<WebhookEvent> DequeueAsync(CancellationToken ct = default);
}
