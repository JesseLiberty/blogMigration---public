using System.Text.Json;
using Microsoft.Extensions.AI;

namespace BlogMigration;

/// <summary>Creates the blogger decision chain.</summary>
public class BloggerChain(IChatClient llm, ChatOptions chatOptions) : IBloggerChain
{
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

        // LLM decision as fallback
        string prompt = Prompts.BloggerPromptTemplate
            .Replace("{main_task}", state.MainTask)
            .Replace("{research_findings}", researchText)
            .Replace("{draft}", string.IsNullOrEmpty(state.Draft) ? "No draft yet." : state.Draft)
            .Replace("{review_notes}", string.IsNullOrEmpty(review) ? "No review yet." : review)
            .Replace("{revision_number}", revision.ToString());

        try
        {
            ChatResponse response = await llm.GetResponseAsync(prompt, chatOptions);
            string content = response.Text;

            // Try to parse JSON
            string text = content.Trim();
            if (text.StartsWith("```"))
            {
                IEnumerable<string> lines = text.Split('\n').Where(l => !l.TrimStart().StartsWith("```"));
                text = string.Join("\n", lines);
            }
            text = text.Trim();

            BloggerDecision? decision = JsonSerializer.Deserialize<BloggerDecision>(text);

            if (decision is not null && !string.IsNullOrEmpty(decision.NextStep))
            {
                return decision;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"LLM parsing error: {e.Message}");
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
