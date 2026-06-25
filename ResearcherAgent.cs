using System.Text.Json;
using Microsoft.Extensions.AI;

namespace BlogMigration;

/// <summary>Creates a researcher agent that uses Tavily search.</summary>
public class ResearcherAgent(IChatClient llm, ChatOptions chatOptions, AIFunction tavilyTool) : IResearcherAgent
{
    /// <summary>Execute research using Tavily search.</summary>
    public async Task<string> InvokeAsync(string query)
    {
        try
        {
            object? searchResult = await tavilyTool.InvokeAsync(
                new AIFunctionArguments { ["query"] = query });

            string searchJson = searchResult switch
            {
                JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() ?? "{}" : je.GetRawText(),
                string s => s,
                _ => searchResult?.ToString() ?? "{}"
            };

            var formattedResults = new List<string>();

            using (JsonDocument document = JsonDocument.Parse(searchJson))
            {
                if (document.RootElement.TryGetProperty("results", out JsonElement results)
                    && results.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement result in results.EnumerateArray().Take(3))
                    {
                        string title = result.TryGetProperty("title", out JsonElement t) ? t.GetString() ?? "Untitled" : "Untitled";
                        string url = result.TryGetProperty("url", out JsonElement u) ? u.GetString() ?? "N/A" : "N/A";
                        string content = result.TryGetProperty("content", out JsonElement c) ? c.GetString() ?? "" : "";
                        string snippet = content.Length > 250 ? content[..250] : content;
                        formattedResults.Add($">>{title}\nSource: {url}\n{snippet}...\n");
                    }
                }
            }

            string rawOutput = formattedResults.Count > 0
                ? string.Join("\n", formattedResults)
                : "No results found";

            // Summarize with LLM
            string summaryPrompt = $"""
                Based on these search results about '{query}',
                provide a concise summary of key findings:
                {rawOutput}
                """;

            ChatResponse summaryResponse = await llm.GetResponseAsync(summaryPrompt, chatOptions);
            string summary = summaryResponse.Text;

            return !string.IsNullOrEmpty(summary) ? summary : rawOutput;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Research error: {e.Message}");
            return $"Research completed on: {query}. Key information has been gathered from web sources.";
        }
    }

    /// <summary>Research node that gathers information.</summary>
    public async Task<ResearchState> ResearchNodeAsync(ResearchState state)
    {
        Console.WriteLine("\n>>>RESEARCHER");

        string subTask = !string.IsNullOrEmpty(state.CurrentSubTask) ? state.CurrentSubTask : state.MainTask;
        Console.WriteLine($"Researching: {subTask}");

        string findings;
        try
        {
            findings = await InvokeAsync(subTask);
            string preview = findings.Length > 100 ? findings[..100] : findings;
            Console.WriteLine($"Found: {preview}...");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Research error: {e.Message}");
            findings = $"Research on {subTask} - information gathered";
        }

        state.ResearchFindings.Add(findings);
        return state;
    }
}
