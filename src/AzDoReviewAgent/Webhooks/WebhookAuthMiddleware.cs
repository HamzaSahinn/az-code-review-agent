using System.Security.Cryptography;
using System.Text;
using AzDoReviewAgent.Configuration;
using Microsoft.Extensions.Options;

namespace AzDoReviewAgent.Webhooks;

/// <summary>
/// ASP.NET Core middleware that enforces HTTP Basic authentication on all
/// requests whose path starts with <c>/api/webhooks/</c>.
/// </summary>
/// <remarks>
/// Credentials are compared with <see cref="CryptographicOperations.FixedTimeEquals"/>
/// to prevent timing-based side-channel attacks.
/// </remarks>
public sealed class WebhookAuthMiddleware
{
    private const string BasicScheme = "Basic";
    private const string WebhookPathPrefix = "/api/webhooks/";

    private readonly RequestDelegate _next;
    private readonly WebhookAuthOptions _options;
    private readonly ILogger<WebhookAuthMiddleware> _logger;

    public WebhookAuthMiddleware(
        RequestDelegate next,
        IOptions<WebhookAuthOptions> options,
        ILogger<WebhookAuthMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only protect webhook paths; let all other routes pass through.
        if (!context.Request.Path.StartsWithSegments(
                WebhookPathPrefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!TryExtractCredentials(context.Request, out var username, out var password))
        {
            _logger.LogWarning("Webhook request from {RemoteIp} missing or malformed Authorization header",
                context.Connection.RemoteIpAddress);
            Reject(context);
            return;
        }

        if (!CredentialsMatch(username, password))
        {
            _logger.LogWarning("Webhook request from {RemoteIp} failed authentication (username: {Username})",
                context.Connection.RemoteIpAddress, username);
            Reject(context);
            return;
        }

        await _next(context);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool TryExtractCredentials(
        HttpRequest request,
        out string username,
        out string password)
    {
        username = string.Empty;
        password = string.Empty;

        var authHeader = request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) ||
            !authHeader.StartsWith(BasicScheme + ' ', StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var encoded = authHeader[(BasicScheme.Length + 1)..].Trim();

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return false;
        }

        var colonIndex = decoded.IndexOf(':');
        if (colonIndex < 0)
            return false;

        username = decoded[..colonIndex];
        password = decoded[(colonIndex + 1)..];
        return true;
    }

    private bool CredentialsMatch(string username, string password)
    {
        var expectedUsername = Encoding.UTF8.GetBytes(_options.Username);
        var expectedPassword = Encoding.UTF8.GetBytes(_options.Password);
        var actualUsername   = Encoding.UTF8.GetBytes(username);
        var actualPassword   = Encoding.UTF8.GetBytes(password);

        // Pad to the same length so FixedTimeEquals always runs full comparison.
        return CryptographicOperations.FixedTimeEquals(
                   PadToLength(actualUsername, expectedUsername.Length),
                   expectedUsername)
               && CryptographicOperations.FixedTimeEquals(
                   PadToLength(actualPassword, expectedPassword.Length),
                   expectedPassword);
    }

    /// <summary>
    /// Returns a byte array of exactly <paramref name="targetLength"/> bytes,
    /// either the original <paramref name="bytes"/> or a zero-padded copy,
    /// so that <see cref="CryptographicOperations.FixedTimeEquals"/> can compare
    /// equal-length spans without short-circuiting on length mismatch.
    /// </summary>
    private static byte[] PadToLength(byte[] bytes, int targetLength)
    {
        if (bytes.Length == targetLength)
            return bytes;

        var padded = new byte[targetLength];
        bytes.AsSpan().Slice(0, Math.Min(bytes.Length, targetLength)).CopyTo(padded);
        return padded;
    }

    private static void Reject(HttpContext context)
    {
        context.Response.Headers.WWWAuthenticate = $"{BasicScheme} realm=\"AzureDevOps Webhooks\"";
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }
}
