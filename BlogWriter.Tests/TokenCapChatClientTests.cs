using Microsoft.Extensions.AI;
using Xunit;

namespace BlogWriter.Tests;

public class TokenCapChatClientTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_ThrowsForNonPositiveCap(long invalidCap)
    {
        using var inner = new FakeChatClient(totalTokens: 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenCapChatClient(inner, invalidCap));
    }

    [Fact]
    public async Task GetResponseAsync_AllowsCallsUnderTheCap()
    {
        using var inner = new FakeChatClient(totalTokens: 40);
        using var client = new TokenCapChatClient(inner, maxTotalTokens: 100);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi again")]);

        // 80 tokens consumed of a 100 cap — no exception expected.
    }

    [Fact]
    public async Task GetResponseAsync_ThrowsOnceCumulativeUsageExceedsCap()
    {
        using var inner = new FakeChatClient(totalTokens: 60);
        using var client = new TokenCapChatClient(inner, maxTotalTokens: 100);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        await Assert.ThrowsAsync<TokenCapExceededException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi again")]));
    }
}
