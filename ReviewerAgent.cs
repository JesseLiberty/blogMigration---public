using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace BlogWriter;

/// <summary>
/// Reviewer chain backed by a Microsoft Agent Framework <see cref="ChatClientAgent"/>.
///
/// MAF idiom change: the evaluation role lives in the agent's Instructions; only
/// the task + draft are sent as the per-turn user message.
///
/// Correctness fix: the previous version's <c>catch</c> returned
/// "APPROVED - Error in review..." — meaning any transient LLM/transport failure
/// would silently approve an unreviewed draft. It now returns a revision request
/// instead, so a failed review re-loops to the author (bounded by
/// <see cref="ResearchState.MaxRevisions"/>) rather than shipping unchecked content.
/// </summary>
public class ReviewerAgent : IReviewerAgent
{
    private readonly ChatClientAgent _agent;

    public ReviewerAgent(IChatClient llm, ChatOptions chatOptions)
    {
        _agent = new ChatClientAgent(llm, new ChatClientAgentOptions
        {
            Name = "Reviewer",
            ChatOptions = new ChatOptions
            {
                Instructions = Prompts.ReviewerInstructions,
                Temperature = chatOptions.Temperature,
                MaxOutputTokens = chatOptions.MaxOutputTokens,
            },
        });
    }

    public async Task<string> InvokeAsync(ResearchState state)
    {
        string draft = state.Draft;
        int revisionNum = state.RevisionNumber;

        if (draft.Trim().Length < 100)
        {
            return "APPROVED - Draft is minimal but acceptable.";
        }

        if (revisionNum >= ResearchState.MaxRevisions)
        {
            return "APPROVED - Maximum revisions reached. The report is satisfactory.";
        }

        // Per-turn input only — the evaluation criteria are on the agent.
        string message = $"""
            Main Task: {state.MainTask}

            Draft to Review:
            {draft}
            """;

        try
        {
            AgentResponse response = await _agent.RunAsync(message);
            string content = response.Text;
            return !string.IsNullOrEmpty(content) ? content : "APPROVED";
        }
        catch (Exception e)
        {
            // Do NOT approve on failure — that would ship an unreviewed draft.
            // Returning feedback (not "APPROVED") routes back to the author for
            // another attempt; the revision cap still guarantees termination.
            Console.WriteLine($"Review error: {e.Message}");
            return "Review could not be completed due to a transient error. Please revise and resubmit the draft.";
        }
    }

    /// <summary>Node that reviews the draft.</summary>
    public async Task<ResearchState> ReviewerNodeAsync(ResearchState state)
    {
        Console.WriteLine("\n>>REVIEWER");

        string review = await InvokeAsync(state);
        string preview = review.Length > 100 ? review[..100] : review;
        Console.WriteLine($"Review: {preview}...");

        bool isApproved = review.ToUpperInvariant().Contains("APPROVED");

        if (isApproved)
        {
            Console.WriteLine("\u2713 Draft APPROVED");
            state.ReviewNotes = "APPROVED";
            state.NextStep = "END";
        }
        else
        {
            Console.WriteLine("\u2717 Revisions needed");
            state.ReviewNotes = review;
            state.NextStep = "author";
        }

        return state;
    }
}
