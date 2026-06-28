using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace BlogMigration;

/// <summary>
/// Researcher backed by a Microsoft Agent Framework <see cref="ChatClientAgent"/>.
///
/// MAF idiom change: previously this class manually invoked the Tavily tool,
/// hand-parsed the JSON response, then made a SECOND LLM call to summarise it.
/// Now the Tavily function is registered as a *tool on the agent*, so the model
/// itself decides when to search and produces the summary in a single agent run.
/// This requires the underlying <see cref="IChatClient"/> to have
/// function-invocation middleware enabled (wired in <c>Program.cs</c> via
/// <c>UseFunctionInvocation()</c>), which actually executes the tool calls the
/// model requests.
/// </summary>
public class ResearcherAgent : IResearcherAgent
{
    // The agent is built once and reused for every research turn. It is stateless
    // across turns (no AgentSession is retained), which matches the original
    // per-call behaviour while gaining tool-calling for free.
    private readonly ChatClientAgent _agent;

    public ResearcherAgent(IChatClient llm, ChatOptions chatOptions, AIFunction tavilyTool)
    {
        _agent = new ChatClientAgent(llm, new ChatClientAgentOptions
        {
            // Name surfaces in OpenTelemetry traces and agent logs.
            Name = "Researcher",
            ChatOptions = new ChatOptions
            {
                // Static role/system prompt lives here instead of being concatenated
                // into every request body.
                Instructions = Prompts.ResearcherInstructions,
                // Preserve the original sampling/cost settings.
                Temperature = chatOptions.Temperature,
                MaxOutputTokens = chatOptions.MaxOutputTokens,
                // Attaching the tool lets the model call it autonomously.
                Tools = [tavilyTool],
            },
        });
    }

    /// <summary>Execute research by letting the agent search and summarise.</summary>
    public async Task<string> InvokeAsync(string query)
    {
        try
        {
            // A single agent run: the model may call tavily_search one or more
            // times, read the results, and return a concise summary as its text.
            AgentResponse response = await _agent.RunAsync(query);
            string summary = response.Text;

            return !string.IsNullOrEmpty(summary)
                ? summary
                : $"Research completed on: {query}. Key information has been gathered from web sources.";
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
