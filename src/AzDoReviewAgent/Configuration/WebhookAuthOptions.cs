namespace AzDoReviewAgent.Configuration;

public sealed class WebhookAuthOptions
{
    public const string SectionName = "WebhookAuth";

    public required string Username { get; set; }
    public required string Password { get; set; }
}
