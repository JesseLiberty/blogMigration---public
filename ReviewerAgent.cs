using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace BlogWriter;

/// <summary>
/// Reviews draft content with a <see cref="ChatClientAgent"/> and returns either
/// approval or revision feedback.
///
/// The review result drives whether the workflow ends or routes back to the
/// author for another iteration.
/// </summary>
public class ReviewerAgent : IReviewerAgent
{
    private readonly ChatClientAgent _agent;

    // Emits a span per review. Activated by the ActivityListener registered in
    // Program.cs (or an OpenTelemetry TracerProvider).
    private static readonly ActivitySource s_activitySource = new("BlogWriter.ReviewerAgent");

    private readonly ILogger<ReviewerAgent> _logger;

    public ReviewerAgent(IChatClient llm, ChatOptions chatOptions, ILogger<ReviewerAgent> logger)
    {
        _logger = logger;

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
        _logger.LogInformation("ReviewerAgent initialized.");
    }

    public async Task<string> InvokeAsync(ResearchState state)
    {
        using Activity? activity = s_activitySource.StartActivity("Reviewer.Invoke");
        activity?.SetTag("blog.revision", state.RevisionNumber);

        string draft = state.Draft;
        int revisionNum = state.RevisionNumber;

        if (revisionNum >= ResearchState.MaxRevisions)
        {
            return "Uh oh - Maximum revisions reached.";
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
        catch (TokenCapExceededException)
        {
            // Budget breach is fatal — let it propagate so the app can shut down.
            throw;
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
