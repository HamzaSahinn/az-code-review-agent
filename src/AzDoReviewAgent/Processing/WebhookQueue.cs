using System.Threading.Channels;
using AzDoReviewAgent.Webhooks.Models;

namespace AzDoReviewAgent.Processing;

/// <summary>
/// Bounded, in-process <see cref="Channel{T}"/>-backed implementation of
/// <see cref="IWebhookQueue"/>.
/// </summary>
/// <remarks>
/// The channel capacity is set to 100 items.  When the channel is full,
/// <see cref="EnqueueAsync"/> will back-pressure the caller (wait) rather than
/// dropping events, which guarantees at-least-once processing as long as the
/// host process is alive.
/// </remarks>
public sealed class WebhookQueue : IWebhookQueue
{
    private readonly Channel<WebhookEvent> _channel;

    public WebhookQueue()
    {
        _channel = Channel.CreateBounded<WebhookEvent>(
            new BoundedChannelOptions(100)
            {
                // Block the writer when the channel is full rather than dropping items.
                FullMode          = BoundedChannelFullMode.Wait,
                // Single reader (the ReviewOrchestrator hosted service).
                SingleReader      = true,
                // Multiple writers (concurrent HTTP requests from the webhook endpoint).
                SingleWriter      = false,
                // Optimise for the common case: AllowSynchronousContinuations keeps
                // latency low but is safe here because the reader never holds locks.
                AllowSynchronousContinuations = false
            });
    }

    /// <inheritdoc/>
    public ValueTask EnqueueAsync(WebhookEvent webhookEvent, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(webhookEvent, ct);

    /// <inheritdoc/>
    public ValueTask<WebhookEvent> DequeueAsync(CancellationToken ct = default)
        => _channel.Reader.ReadAsync(ct);
}
