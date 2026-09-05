using System.ClientModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BlogWriter;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using OpenAI;

// Secrets come from the .NET user-secrets store and from
// environment variables (secrets win on key collisions).
IConfiguration config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

string GetRequired(string key) =>
    config[key] ?? throw new InvalidOperationException(
        $"Missing configuration value '{key}'. Set it with: dotnet user-secrets set \"{key}\" \"<value>\"");

string openAiApiKey = GetRequired("API_KEY");
string openAiApiBase = GetRequired("OPENAI_BASE_URL");
string tavilyApiKey = GetRequired("TAVILY_API_KEY");

// Overridable via user-secrets/env vars; these defaults match the original behaviour.
string modelName = config["MODEL_NAME"] ?? "gpt-5-mini";
int maxOutputTokens = int.TryParse(config["MAX_OUTPUT_TOKENS"], out int configuredMaxOutputTokens) ? configuredMaxOutputTokens : 4096;
long maxTotalTokens = long.TryParse(config["MAX_TOTAL_TOKENS"], out long configuredMaxTotalTokens) ? configuredMaxTotalTokens : 40000;
if (!Uri.TryCreate(openAiApiBase, UriKind.Absolute, out var uri))
{
    throw new InvalidOperationException($"Invalid URI: '{openAiApiBase}'");
}
var openAIClient = new OpenAIClient(
    new ApiKeyCredential(openAiApiKey),
    new OpenAIClientOptions
    {
        Endpoint = new Uri(openAiApiBase),
        // The SDK's default RetryPolicy still applies on top of this; this only
        // bounds how long a single network attempt can hang before it retries/fails.
        NetworkTimeout = TimeSpan.FromSeconds(60),
    });

// Build the IChatClient pipeline once and share it across all agents.
// UseFunctionInvocation() adds the middleware that actually *executes* the tool
// calls the model requests — without it, attaching the Tavily tool to the
// Researcher agent would let the model ask for a search but nothing would run it.
//
// UseOpenTelemetry() emits a GenAI span per model round-trip (model name, token
// usage, tool calls). Its source is named "BlogWriter.ChatClient" so the
// ActivityListener registered below (which listens to every "BlogWriter.*"
// source) captures it alongside the agent/workflow spans — no TracerProvider
// or extra packages required.
//
// TokenCapChatClient is registered *after* function invocation, which makes it
// the innermost wrapper around the raw client — so it observes every individual
// model round-trip (including the extra calls tool invocation triggers) and
// enforces a hard cumulative-token budget for the whole process.
TokenCapChatClient? tokenCapChatClient = null;
IChatClient llm = openAIClient
    .GetChatClient(modelName)
    .AsIChatClient()
    .AsBuilder()
    .UseFunctionInvocation()
    .UseOpenTelemetry(sourceName: "BlogWriter.ChatClient")
    .Use(inner => tokenCapChatClient = new TokenCapChatClient(inner, maxTotalTokens))
    .Build();

var chatOptions = new ChatOptions
{
    Temperature = 1,
    MaxOutputTokens = maxOutputTokens
};

var tavilyHttpClient = new HttpClient { BaseAddress = new Uri("https://api.tavily.com/"), Timeout = TimeSpan.FromSeconds(20) };
tavilyHttpClient.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", tavilyApiKey);

// Small manual retry: transient network errors/timeouts get up to 2 retries
// with exponential backoff before the failure surfaces to the calling agent.
async Task<HttpResponseMessage> PostWithRetryAsync(string requestUri, object body, CancellationToken cancellationToken)
{
    const int maxAttempts = 3;
    for (int attempt = 1; ; attempt++)
    {
        try
        {
            HttpResponseMessage response = await tavilyHttpClient.PostAsJsonAsync(requestUri, body, cancellationToken);
            response.EnsureSuccessStatusCode();
            return response;
        }
        catch (Exception ex) when (attempt < maxAttempts && ex is HttpRequestException or TaskCanceledException)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
        }
    }
}

AIFunction tavilyTool = AIFunctionFactory.Create(
    async (string query, CancellationToken cancellationToken) =>
    {
        var request = new
        {
            query,
            max_results = 5,
            topic = "general",
            include_answer = false,
            include_raw_content = false,
            search_depth = "basic"
        };

        using HttpResponseMessage response = await PostWithRetryAsync("search", request, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    },
    name: "tavily_search",
    description: "A search engine optimized for comprehensive, accurate, and trusted results.");

// Microsoft Learn's remote MCP server exposes docs search/fetch tools the
// Researcher can call alongside Tavily for authoritative Microsoft/Azure content.
await using McpClient microsoftLearnMcp = await McpClient.CreateAsync(
    new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri("https://learn.microsoft.com/api/mcp"),
        Name = "microsoft-learn",
    }));
IList<McpClientTool> microsoftLearnTools = await microsoftLearnMcp.ListToolsAsync();

List<AIFunction> researcherTools = [tavilyTool, .. microsoftLearnTools];

// Creating a callable object
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var bloggerAgent = new BloggerAgent(llm, chatOptions, loggerFactory.CreateLogger<BloggerAgent>());
var researcherAgent = new ResearcherAgent(llm, chatOptions, researcherTools, loggerFactory.CreateLogger<ResearcherAgent>());
var authorAgent = new AuthorAgent(llm, chatOptions, loggerFactory.CreateLogger<AuthorAgent>());
var reviewerAgent = new ReviewerAgent(llm, chatOptions, loggerFactory.CreateLogger<ReviewerAgent>());
var app = new BlogWorkflow(bloggerAgent, researcherAgent, authorAgent, reviewerAgent, loggerFactory.CreateLogger<BlogWorkflow>());

// Distributed tracing: an ActivityListener activates every "BlogWriter.*"
// ActivitySource in the app (agents, workflow, and the IChatClient's
// "BlogWriter.ChatClient" GenAI spans) and writes span start/stop to the
// console. Swap this listener for OpenTelemetry's TracerProvider to export the
// same spans to a backend instead.
var appActivitySource = new ActivitySource("BlogWriter.Program");

ActivitySource.AddActivityListener(new ActivityListener
{
    ShouldListenTo = source => source.Name.StartsWith("BlogWriter", StringComparison.Ordinal),
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStarted = activity => Console.WriteLine($"[trace] \u2192 {activity.DisplayName}"),
    ActivityStopped = activity =>
        Console.WriteLine($"[trace] \u2190 {activity.DisplayName} ({activity.Duration.TotalMilliseconds:F0} ms)")
});

Console.Write("Enter your topic: ");
string topic = Console.ReadLine() ?? string.Empty;

int minWords = ReadWordCount(
    $"Enter minimum word count [{ResearchState.DefaultMinWords}]: ",
    ResearchState.DefaultMinWords);
int maxWords = ReadWordCount(
    $"Enter maximum word count [{ResearchState.DefaultMaxWords}]: ",
    ResearchState.DefaultMaxWords,
    minimum: minWords);

// Prompts for a positive word count, re-asking until a valid value (or blank
// for the default) is entered. `minimum`, when set, enforces max >= min.
int ReadWordCount(string prompt, int defaultValue, int? minimum = null)
{
    while (true)
    {
        Console.Write(prompt);
        string? input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return defaultValue;
        }

        if (int.TryParse(input, out int value) && value > 0 && (minimum is null || value >= minimum))
        {
            return value;
        }

        Console.WriteLine(minimum is null
            ? "Please enter a positive whole number."
            : $"Please enter a whole number greater than or equal to {minimum}.");
    }
}

// Run the workflow for the entered topic
var initialState = new ResearchState
{
    MainTask = topic,
    MinWords = minWords,
    MaxWords = maxWords
};

// Ctrl+C requests a graceful cancellation of the in-flight run instead of an
// abrupt process kill.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

ResearchState result;
try
{
    using Activity? runActivity = appActivitySource.StartActivity("BlogWriter.Run");
    runActivity?.SetTag("blog.topic", topic);
    result = await app.RunAsync(initialState, cts.Token);
}
catch (TokenCapExceededException ex)
{
    // Graceful shutdown: the exception unwinds the call stack so every `using`
    // (logger factory, HTTP clients, etc.) is disposed before we exit.
    Console.Error.WriteLine($"{ex.Message} Exiting application.");
    Environment.ExitCode = 1;
    return;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Run cancelled. Exiting application.");
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("\n========== RESULTS ==========");
Console.WriteLine($"Task: {result.MainTask}");

Console.WriteLine($"\nResearch Findings ({result.ResearchFindings.Count}):");
foreach (string finding in result.ResearchFindings)
{
    Console.WriteLine($"- {finding}");
}

Console.WriteLine($"\n\n========== Draft ==========\n\n{result.Draft}");
Console.WriteLine($"\n========== Review Notes ==========\n{result.ReviewNotes}");
Console.WriteLine($"\n========== Revision Notes ==========\n{result.RevisionNumber}");
if (result.RevisionNumber >= ResearchState.MaxRevisions)
{
    // The revision cap terminates the loop even if the reviewer never approved —
    // call that out so the draft above isn't mistaken for a reviewer-approved one.
    Console.WriteLine("Note: Maximum revision limit reached; draft above printed as-is.");
}
Console.WriteLine("\n=============================\n");

if (tokenCapChatClient is not null)
{
    TokenUsageSnapshot usage = tokenCapChatClient.UsageSnapshot;
    Console.WriteLine("\n========== TOKEN USAGE ==========");
    Console.WriteLine($"Input tokens:     {usage.InputTokens}");
    Console.WriteLine($"Output tokens:    {usage.OutputTokens}");
    Console.WriteLine($"Reasoning tokens: {usage.ReasoningTokens}");
    Console.WriteLine($"Total tokens:     {usage.TotalTokens}");
    Console.WriteLine("==================================");
}

