using System.ClientModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlogWriter;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

const string fileName = "config.json";

using var stream = File.OpenRead(fileName);
using var document = JsonDocument.Parse(stream);
JsonElement config = document.RootElement;

string? GetValue(string key) =>
    config.TryGetProperty(key, out JsonElement value) ? value.GetString() : null;

Environment.SetEnvironmentVariable("OPENAI_API_KEY", GetValue("API_KEY"));
Environment.SetEnvironmentVariable("OPENAI_BASE_URL", GetValue("OPENAI_API_BASE"));
Environment.SetEnvironmentVariable("TAVILY_API_KEY", GetValue("TAVILY_API_KEY"));

string modelName = "gpt-4o-mini";

var openAIClient = new OpenAIClient(
    new ApiKeyCredential(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!),
    new OpenAIClientOptions
    {
        Endpoint = new Uri(Environment.GetEnvironmentVariable("OPENAI_BASE_URL")!)
    });

// Build the IChatClient pipeline once and share it across all agents.
// UseFunctionInvocation() adds the middleware that actually *executes* the tool
// calls the model requests — without it, attaching the Tavily tool to the
// Researcher agent would let the model ask for a search but nothing would run it.
// Middleware is applied inner-to-outer, so function invocation wraps the raw
// OpenAI client. (To add distributed tracing later, chain .UseOpenTelemetry()
// here and register the source with a TracerProvider.)
IChatClient llm = openAIClient
    .GetChatClient(modelName)
    .AsIChatClient()
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

var chatOptions = new ChatOptions
{
    Temperature = 0,
    MaxOutputTokens = 4096
};

var tavilyHttpClient = new HttpClient { BaseAddress = new Uri("https://api.tavily.com/") };
tavilyHttpClient.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", Environment.GetEnvironmentVariable("TAVILY_API_KEY"));

AIFunction tavilyTool = AIFunctionFactory.Create(
    async (string query) =>
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

        using HttpResponseMessage response = await tavilyHttpClient.PostAsJsonAsync("search", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    },
    name: "tavily_search",
    description: "A search engine optimized for comprehensive, accurate, and trusted results.");

// Creating a callable object
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var bloggerAgent = new BloggerAgent(llm, chatOptions, loggerFactory.CreateLogger<BloggerAgent>());
var researcherAgent = new ResearcherAgent(llm, chatOptions, tavilyTool, loggerFactory.CreateLogger<ResearcherAgent>());
var authorAgent = new AuthorAgent(llm, chatOptions, loggerFactory.CreateLogger<AuthorAgent>());
var reviewerAgent = new ReviewerAgent(llm, chatOptions, loggerFactory.CreateLogger<ReviewerAgent>());
var app = new BlogWorkflow(bloggerAgent, researcherAgent, authorAgent, reviewerAgent);

// Distributed tracing: an ActivityListener activates every "BlogWriter.*"
// ActivitySource in the app (agents + workflow) and writes span start/stop to
// the console. Swap this listener for OpenTelemetry's TracerProvider (and chain
// IChatClient.UseOpenTelemetry() above) to export the same spans instead.
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

// Run the workflow for the entered topic
var initialState = new ResearchState
{
    MainTask = topic
};

ResearchState result;
using (Activity? runActivity = appActivitySource.StartActivity("BlogWriter.Run"))
{
    runActivity?.SetTag("blog.topic", topic);
    result = await app.RunAsync(initialState);
}

Console.WriteLine("\n========== RESULTS ==========");
Console.WriteLine($"Task: {result.MainTask}");

Console.WriteLine($"\nResearch Findings ({result.ResearchFindings.Count}):");
foreach (string finding in result.ResearchFindings)
{
    Console.WriteLine($"- {finding}");
}

Console.WriteLine($"\nDraft:\n{result.Draft}");
Console.WriteLine($"\nReview Notes: {result.ReviewNotes}");
Console.WriteLine($"Revision Number: {result.RevisionNumber}");
Console.WriteLine("=============================");

