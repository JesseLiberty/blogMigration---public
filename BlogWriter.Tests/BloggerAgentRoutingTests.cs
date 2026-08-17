using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlogWriter.Tests;

/// <summary>
/// Verifies the Blogger's deterministic C# routing rules never reach the model
/// (a <see cref="ThrowingChatClient"/> fails the test if the LLM path is hit).
/// </summary>
public class BloggerAgentRoutingTests
{
    private static BloggerAgent CreateAgent() =>
        new(new ThrowingChatClient(), new ChatOptions { Temperature = 0, MaxOutputTokens = 100 }, NullLogger<BloggerAgent>.Instance);

    [Fact]
    public async Task NoResearch_RoutesToResearcher()
    {
        BloggerDecision decision = await CreateAgent().InvokeAsync(new ResearchState { MainTask = "topic" });

        Assert.Equal("researcher", decision.NextStep);
    }

    [Fact]
    public async Task ResearchButNoDraft_RoutesToAuthor()
    {
        var state = new ResearchState { MainTask = "topic", ResearchFindings = ["some findings"] };

        BloggerDecision decision = await CreateAgent().InvokeAsync(state);

        Assert.Equal("author", decision.NextStep);
    }

    [Fact]
    public async Task DraftWithNoReview_RoutesToReviewer()
    {
        var state = new ResearchState
        {
            MainTask = "topic",
            ResearchFindings = ["some findings"],
            Draft = "a draft",
        };

        BloggerDecision decision = await CreateAgent().InvokeAsync(state);

        Assert.Equal("reviewer", decision.NextStep);
    }

    [Fact]
    public async Task DraftWithRevisionFeedback_RoutesBackToAuthor()
    {
        var state = new ResearchState
        {
            MainTask = "topic",
            ResearchFindings = ["some findings"],
            Draft = "a draft",
            ReviewNotes = "Please tighten the intro.",
            RevisionNumber = 1,
        };

        BloggerDecision decision = await CreateAgent().InvokeAsync(state);

        Assert.Equal("author", decision.NextStep);
    }

    [Fact]
    public async Task ApprovedDraft_Ends()
    {
        var state = new ResearchState
        {
            MainTask = "topic",
            ResearchFindings = ["some findings"],
            Draft = "a draft",
            ReviewNotes = ResearchState.ApprovedMarker,
        };

        BloggerDecision decision = await CreateAgent().InvokeAsync(state);

        Assert.Equal("END", decision.NextStep);
    }

    [Fact]
    public async Task RevisionCapReached_Ends()
    {
        var state = new ResearchState
        {
            MainTask = "topic",
            ResearchFindings = ["some findings"],
            Draft = "a draft",
            ReviewNotes = "Still not good enough.",
            RevisionNumber = ResearchState.MaxRevisions,
        };

        BloggerDecision decision = await CreateAgent().InvokeAsync(state);

        Assert.Equal("END", decision.NextStep);
    }
}
