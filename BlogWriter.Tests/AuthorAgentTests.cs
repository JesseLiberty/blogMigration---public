using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlogWriter.Tests;

/// <summary>Verifies the Author stage always leaves the workflow with a non-empty draft.</summary>
public class AuthorAgentTests
{
    private static AuthorAgent CreateAgent(IChatClient chatClient) =>
        new(chatClient, new ChatOptions { Temperature = 0, MaxOutputTokens = 100 }, NullLogger<AuthorAgent>.Instance);

    [Fact]
    public async Task ModelReturnsEmpty_AndNoPriorDraft_UsesFallbackDraft()
    {
        var state = new ResearchState { MainTask = "topic", ResearchFindings = ["some findings"] };

        state = await CreateAgent(new EmptyChatClient()).AuthorNodeAsync(state);

        Assert.False(string.IsNullOrEmpty(state.Draft));
        Assert.Contains("some findings", state.Draft);
    }

    [Fact]
    public async Task ModelReturnsEmpty_ButPriorDraftExists_KeepsPriorDraft()
    {
        var state = new ResearchState { MainTask = "topic", Draft = "existing draft" };

        state = await CreateAgent(new EmptyChatClient()).AuthorNodeAsync(state);

        Assert.Equal("existing draft", state.Draft);
    }

    [Fact]
    public async Task ModelReturnsContent_UsesModelDraft()
    {
        var state = new ResearchState { MainTask = "topic" };

        state = await CreateAgent(new FakeChatClient(10)).AuthorNodeAsync(state);

        Assert.Equal("ok", state.Draft);
    }
}
