namespace AzDoReviewAgent.Configuration;

public sealed class AzureDevOpsOptions
{
    public const string SectionName = "AzureDevOps";

    public required string ServerUrl { get; set; }
    public required string Collection { get; set; }
    public required string Pat { get; set; }
    public string ApiVersion { get; set; } = "7.0";
    public required string AgentUserId { get; set; }
    public required string AgentDisplayName { get; set; }
}
