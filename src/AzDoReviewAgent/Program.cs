using AzDoReviewAgent.Configuration;
using AzDoReviewAgent.AzureDevOps;
using AzDoReviewAgent.Diff;
using AzDoReviewAgent.Processing;
using AzDoReviewAgent.Review;
using AzDoReviewAgent.Webhooks;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Polly;
using Polly.Extensions.Http;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // Configuration
    builder.Services.Configure<AzureDevOpsOptions>(builder.Configuration.GetSection(AzureDevOpsOptions.SectionName));
    builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
    builder.Services.Configure<WebhookAuthOptions>(builder.Configuration.GetSection(WebhookAuthOptions.SectionName));
    builder.Services.Configure<ReviewOptions>(builder.Configuration.GetSection(ReviewOptions.SectionName));

    // Azure DevOps HTTP client with retry policy
    builder.Services.AddHttpClient<IAzureDevOpsClient, AzureDevOpsClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<AzureDevOpsOptions>>().Value;
        client.BaseAddress = new Uri($"{options.ServerUrl.TrimEnd('/')}/{options.Collection}/");
        var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($":{options.Pat}"));
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    })
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

    // Semantic Kernel
    builder.Services.AddSingleton(sp =>
    {
        var aiOptions = sp.GetRequiredService<IOptions<AiOptions>>().Value;
        var kernelBuilder = Kernel.CreateBuilder();

        if (aiOptions.Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            kernelBuilder.AddAzureOpenAIChatCompletion(
                deploymentName: aiOptions.AzureOpenAi.DeploymentName,
                endpoint: aiOptions.AzureOpenAi.Endpoint,
                apiKey: aiOptions.AzureOpenAi.ApiKey);
        }
        else
        {
            // Ollama / vLLM — OpenAI-compatible endpoint
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: aiOptions.Ollama.ModelId,
                endpoint: new Uri(aiOptions.Ollama.Endpoint),
                apiKey: "not-required");
        }

        return kernelBuilder.Build();
    });

    // Core services
    builder.Services.AddSingleton<IWebhookQueue, WebhookQueue>();
    builder.Services.AddSingleton<TriggerDetector>();
    builder.Services.AddScoped<IDiffService, DiffService>();
    builder.Services.AddScoped<IReviewService, ReviewService>();
    builder.Services.AddHostedService<ReviewOrchestrator>();

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    // Webhook endpoint
    app.MapWebhookEndpoints();

    // Health check
    app.MapGet("/health", async (IAzureDevOpsClient client) =>
    {
        try
        {
            var healthy = await client.ValidateConnectionAsync();
            return healthy ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(503);
        }
        catch (Exception ex)
        {
            return Results.Json(new { status = "unhealthy", error = ex.Message }, statusCode: 503);
        }
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
