using System.ClientModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlogMigration;
using Microsoft.Extensions.AI;
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

IChatClient llm = openAIClient.GetChatClient(modelName).AsIChatClient();

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
var bloggerChain = new BloggerChain(llm, chatOptions);
var researcherAgent = new ResearcherAgent(llm, chatOptions, tavilyTool);
var authorChain = new AuthorChain(llm, chatOptions);
var reviewerChain = new ReviewerChain(llm, chatOptions);
var app = new BlogWorkflow(bloggerChain, researcherAgent, authorChain, reviewerChain);

// Run the workflow for a sample topic
var initialState = new ResearchState
{
    MainTask = "use of multiagents in writing a C# application"
};

ResearchState result = await app.RunAsync(initialState);

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

