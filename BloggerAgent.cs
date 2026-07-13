using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace BlogWriter;

/// <summary>
/// Determines the next workflow action and subtask based on the current
/// <see cref="ResearchState"/>.
///
/// The agent applies deterministic routing rules first, then uses structured
/// model output as fallback to produce a typed <see cref="BloggerDecision"/>.
/// </summary>
public class BloggerAgent : IBloggerAgent
{
    // Built once and reused. Holds the static Blogger instructions; the volatile
    // state is passed per-turn as the user message.
    private readonly ChatClientAgent _agent;

    // Web-style options are sufficient: BloggerDecision carries explicit
    // [JsonPropertyName] attributes (next_step / task_description) that drive the
    // generated schema regardless of naming policy.
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public BloggerAgent(IChatClient llm, ChatOptions chatOptions)
    {
        _agent = new ChatClientAgent(llm, new ChatClientAgentOptions
        {
            Name = "Blogger",
            ChatOptions = new ChatOptions
            {
                Instructions = Prompts.BloggerInstructions,
                Temperature = chatOptions.Temperature,
                MaxOutputTokens = chatOptions.MaxOutputTokens,
            },
        });
    }

    public async Task<BloggerDecision> InvokeAsync(ResearchState state)
    {
        List<string> research = state.ResearchFindings;
        string researchText = research.Count > 0 ? string.Join("\n", research) : "No research yet.";
        int revision = state.RevisionNumber;
        bool hasResearch = research.Count > 0;
        bool hasDraft = !string.IsNullOrWhiteSpace(state.Draft);
        string review = state.ReviewNotes;

        if (review.ToUpperInvariant().Contains("APPROVED") && hasDraft)
        {
            Console.WriteLine("Blogger: Draft approved, ending workflow");
            return new BloggerDecision("END", "Report approved and complete");
        }

        if (!hasResearch)
        {
            Console.WriteLine("Blogger: No research yet, directing to researcher");
            return new BloggerDecision("researcher", $"Research the topic: {state.MainTask}");
        }

        if (hasResearch && !hasDraft)
        {
            Console.WriteLine("Blogger: Have research, creating first draft");
            return new BloggerDecision("author", "Write the first draft based on research findings");
        }

        if (hasDraft && string.IsNullOrEmpty(review))
        {
            Console.WriteLine("Blogger: Have draft, sending to reviewer");
            return new BloggerDecision("reviewer", "Prepare draft for review");
        }

        if (!string.IsNullOrEmpty(review) && !review.ToUpperInvariant().Contains("APPROVED") && revision < ResearchState.MaxRevisions)
        {
            Console.WriteLine($"Blogger: Revision {revision}, sending back to author");
            return new BloggerDecision("author", "Revise the draft based on review feedback");
        }

        // Max revisions reached
        if (revision >= ResearchState.MaxRevisions)
        {
            Console.WriteLine("Blogger: Max revisions reached! Ending");
            return new BloggerDecision("END", "Maximum revisions reached! Finalizing report");
        }

        // LLM decision as fallback. The dynamic state is the user message; the
        // static role lives in the agent's Instructions. MAF structured output
        // hands back a typed BloggerDecision — no fenced-block cleanup, no manual
        // JsonSerializer.Deserialize.
        string stateSummary = $"""
            Current Task: {state.MainTask}
            Research Findings: {researchText}
            Blog Draft: {(string.IsNullOrEmpty(state.Draft) ? "No draft yet." : state.Draft)}
            Reviewer Feedback: {(string.IsNullOrEmpty(review) ? "No review yet." : review)}
            Revision Number: {revision}
            """;

        try
        {
            AgentResponse<BloggerDecision> response =
                await _agent.RunAsync<BloggerDecision>(stateSummary, serializerOptions: _jsonOptions);

            BloggerDecision decision = response.Result;
            if (decision is not null && !string.IsNullOrEmpty(decision.NextStep))
            {
                return decision;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"LLM decision error: {e.Message}");
        }

        // Final fallback - continue with author
        Console.WriteLine("Blogger: Using final fallback - continuing with author");
        return new BloggerDecision("author", "Continue with draft creation");
    }

    /// <summary>Blogger decides the next step.</summary>
    public async Task<ResearchState> BloggerNodeAsync(ResearchState state)
    {
        Console.WriteLine("\n>>>Blogger");

        BloggerDecision decision = await InvokeAsync(state);

        string nextStep = string.IsNullOrEmpty(decision.NextStep) ? "researcher" : decision.NextStep;
        string taskDesc = string.IsNullOrEmpty(decision.TaskDescription) ? "Continue work" : decision.TaskDescription;

        Console.WriteLine($"Decision: {nextStep}");
        Console.WriteLine($"Task: {taskDesc}");

        state.NextStep = nextStep;
        state.CurrentSubTask = taskDesc;
        return state;
    }
}
