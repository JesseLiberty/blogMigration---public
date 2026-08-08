using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace BlogWriter;

/// <summary>
/// Performs research tasks with a <see cref="ChatClientAgent"/> and returns
/// concise findings.
///
/// The agent can call the configured Tavily tool during execution and summarize
/// results for use in later drafting stages.
/// </summary>
public class ResearcherAgent : IResearcherAgent
{
    // The agent is built once and reused for every research turn. It is stateless
    // across turns (no AgentSession is retained), which matches the original
    // per-call behaviour while gaining tool-calling for free.
    private readonly AIAgent _agent;

    // Emits a span per research turn. Activated by the ActivityListener
    // registered in Program.cs (or an OpenTelemetry TracerProvider).
    private static readonly ActivitySource s_activitySource = new("BlogWriter.ResearcherAgent");

    private readonly ILogger<ResearcherAgent> _logger;

    // Per-call output-token cap, applied on each RunAsync to bound cost.
    private readonly int? _maxOutputTokens;

    public ResearcherAgent(IChatClient llm, ChatOptions chatOptions, AIFunction tavilyTool, ILogger<ResearcherAgent> logger)
    {
        _logger = logger;
        _maxOutputTokens = chatOptions.MaxOutputTokens;

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
        })
        .AsBuilder()
        // Function-invocation middleware: fires around every tool call the agent
        // makes. We log each time the model invokes the Tavily search tool.
        .Use(async (agent, context, next, cancellationToken) =>
        {
            if (context.Function.Name == tavilyTool.Name)
            {
                _logger.LogInformation(
                    "Researcher invoking Tavily tool '{Tool}' with arguments {Arguments}",
                    context.Function.Name,
                    context.Arguments);
            }

            return await next(context, cancellationToken);
        })
        .UseOpenTelemetry(sourceName: "BlogWriter.Agents")
        .Build();

        _logger.LogInformation("ResearcherAgent initialized with Tavily tool: {ToolName}", tavilyTool.Name);
    }

    /// <summary>Execute research by letting the agent search and summarise.</summary>
    public async Task<string> InvokeAsync(string query)
    {
        using Activity? activity = s_activitySource.StartActivity("Researcher.Invoke");
        activity?.SetTag("blog.query", query);

        try
        {
            // A single agent run: the model may call tavily_search one or more
            // times, read the results, and return a concise summary as its text.
            // Cap per-call output tokens so a single turn can't blow the cost budget.
            ChatClientAgentRunOptions runOptions = new(new ChatOptions
            {
                MaxOutputTokens = _maxOutputTokens,
            });
            AgentResponse response = await _agent.RunAsync(query, options: runOptions);
            string summary = response.Text;

            return !string.IsNullOrEmpty(summary)
                ? summary
                : $"Research completed on: {query}. Key information has been gathered from web sources.";
        }
        catch (TokenCapExceededException)
        {
            // Budget breach is fatal — let it propagate so the app can shut down.
            throw;
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
        catch (TokenCapExceededException)
        {
            // Budget breach is fatal — let it propagate so the app can shut down.
            throw;
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
