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
    private readonly AIAgent _agent;

    // Emits a span per review. Activated by the ActivityListener registered in
    // Program.cs (or an OpenTelemetry TracerProvider).
    private static readonly ActivitySource s_activitySource = new("BlogWriter.ReviewerAgent");

    private readonly ILogger<ReviewerAgent> _logger;

    // Per-call output-token cap, applied on each RunAsync to bound cost.
    private readonly int? _maxOutputTokens;

    public ReviewerAgent(IChatClient llm, ChatOptions chatOptions, ILogger<ReviewerAgent> logger)
    {
        _logger = logger;
        _maxOutputTokens = chatOptions.MaxOutputTokens;

        _agent = new ChatClientAgent(llm, new ChatClientAgentOptions
        {
            Name = "Reviewer",
            ChatOptions = new ChatOptions
            {
                Instructions = Prompts.ReviewerInstructions,
                Temperature = chatOptions.Temperature,
                MaxOutputTokens = chatOptions.MaxOutputTokens,
            },
        })
        .AsBuilder()
        .UseOpenTelemetry(sourceName: "BlogWriter.Agents")
        .Build();
        _logger.LogInformation("ReviewerAgent initialized.");
    }

    public async Task<string> InvokeAsync(ResearchState state, CancellationToken cancellationToken = default)
    {
        using Activity? activity = s_activitySource.StartActivity("Reviewer.Invoke");
        activity?.SetTag("blog.revision", state.RevisionNumber);

        string draft = state.Draft;
        int revisionNum = state.RevisionNumber;

        if (revisionNum >= ResearchState.MaxRevisions)
        {
            return "APPROVED - Maximum revisions reached.";
        }

        // Per-turn input only — the evaluation criteria are on the agent.
        string message = $"""
            Main Task: {state.MainTask}

            Draft to Review:
            {draft}
            """;

        try
        {
            // Cap per-call output tokens so a single turn can't blow the cost budget.
            ChatClientAgentRunOptions runOptions = new(new ChatOptions
            {
                MaxOutputTokens = _maxOutputTokens,
            });
            AgentResponse response = await _agent.RunAsync(message, options: runOptions, cancellationToken: cancellationToken);
            string content = response.Text;
            return !string.IsNullOrEmpty(content) ? content : ManageError("No review content returned from the agent.");
        }
        catch (TokenCapExceededException)
        {
            // Budget breach is fatal — let it propagate so the app can shut down.
            throw;
        }
        catch (Exception e)
        {
            return ManageError(e.Message, e);
        }
    }

    private string ManageError(string reason, Exception? exception = null)
    {
        // Do NOT approve on failure — that would ship an unreviewed draft.
        // Returning feedback (not "APPROVED") routes back to the author for
        // another attempt; the revision cap still guarantees termination.
        if (exception is not null)
        {
            _logger.LogError(exception, "Review failed: {Reason}", reason);
        }
        else
        {
            _logger.LogWarning("Review could not be completed: {Reason}", reason);
        }

        return "Review could not be completed due to a transient error. Please revise and resubmit the draft.";
    }

    /// <summary>Node that reviews the draft.</summary>
    public async Task<ResearchState> ReviewerNodeAsync(ResearchState state, CancellationToken cancellationToken = default)
    {
        string review = await InvokeAsync(state, cancellationToken);
        string preview = review.Length > 100 ? review[..100] : review;
        _logger.LogInformation("Review: {Preview}...", preview);

        bool isApproved = ResearchState.IsApproved(review);

        if (isApproved)
        {
            _logger.LogInformation("Draft APPROVED");
            state.ReviewNotes = ResearchState.ApprovedMarker;
            state.NextStep = "END";
        }
        else
        {
            _logger.LogInformation("Revisions needed");
            state.ReviewNotes = review;
            state.NextStep = "author";
        }

        return state;
    }
}
