using System.Text.RegularExpressions;
using AzDoReviewAgent.Configuration;
using Microsoft.Extensions.Options;

namespace AzDoReviewAgent.Processing;

/// <summary>
/// Inspects the plain-text content of a pull-request comment and determines
/// whether the agent should act on it, and if so – how.
/// </summary>
public sealed partial class TriggerDetector
{
    // Matches /review or /ask (with optional trailing text for /ask)
    // Examples:  "/review"  "  /ask is this thread safe?"
    [GeneratedRegex(@"^\s*\/(?<cmd>review|ask)\b(?<rest>.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SlashCommandRegex();

    private readonly ReviewOptions _reviewOptions;
    private readonly AzureDevOpsOptions _azureDevOpsOptions;

    public TriggerDetector(
        IOptions<ReviewOptions> reviewOptions,
        IOptions<AzureDevOpsOptions> azureDevOpsOptions)
    {
        _reviewOptions      = reviewOptions.Value;
        _azureDevOpsOptions = azureDevOpsOptions.Value;
    }

    /// <summary>
    /// Analyses <paramref name="commentContent"/> and returns the appropriate
    /// <see cref="TriggerResult"/>.
    /// </summary>
    public TriggerResult DetectTrigger(string commentContent)
    {
        if (string.IsNullOrWhiteSpace(commentContent))
            return TriggerResult.None;

        // ── 1. Slash-command check ────────────────────────────────────────────
        var match = SlashCommandRegex().Match(commentContent.TrimStart());
        if (match.Success)
        {
            var cmd = match.Groups["cmd"].Value.ToLowerInvariant();

            if (cmd == "ask")
            {
                var question = match.Groups["rest"].Value.Trim();
                return new TriggerResult
                {
                    IsTrigger         = true,
                    TriggerType       = TriggerType.Ask,
                    ExtractedQuestion = string.IsNullOrWhiteSpace(question) ? null : question
                };
            }

            // cmd == "review"
            return new TriggerResult
            {
                IsTrigger   = true,
                TriggerType = TriggerType.Review
            };
        }

        // ── 2. @mention check ────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(_azureDevOpsOptions.AgentDisplayName) &&
            commentContent.Contains(_azureDevOpsOptions.AgentDisplayName,
                StringComparison.OrdinalIgnoreCase))
        {
            return new TriggerResult
            {
                IsTrigger   = true,
                TriggerType = TriggerType.Mention
            };
        }

        return TriggerResult.None;
    }
}

// ── Result types ─────────────────────────────────────────────────────────────

/// <summary>The result of a <see cref="TriggerDetector.DetectTrigger"/> call.</summary>
public sealed class TriggerResult
{
    /// <summary>Sentinel no-op result; avoids allocations for the common "no trigger" path.</summary>
    public static readonly TriggerResult None = new() { IsTrigger = false, TriggerType = TriggerType.None };

    /// <summary>Whether the comment matches a recognised trigger pattern.</summary>
    public bool IsTrigger { get; init; }

    /// <summary>The kind of trigger detected.</summary>
    public TriggerType TriggerType { get; init; }

    /// <summary>
    /// The question text extracted from a <c>/ask</c> command, or <see langword="null"/>
    /// when the trigger is not an /ask command.
    /// </summary>
    public string? ExtractedQuestion { get; init; }
}

/// <summary>The kind of action the agent should take when a trigger is detected.</summary>
public enum TriggerType
{
    /// <summary>No trigger was matched.</summary>
    None = 0,

    /// <summary>A <c>/review</c> slash command requesting a full diff review.</summary>
    Review,

    /// <summary>A <c>/ask</c> slash command requesting a free-form answer.</summary>
    Ask,

    /// <summary>An @mention of the agent's display name.</summary>
    Mention
}
