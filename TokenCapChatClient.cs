using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace BlogWriter;

/// <summary>
/// Chat-client middleware that enforces a hard cap on cumulative token usage
/// across every model round-trip in the process (including the extra calls made
/// during tool invocation). When the running total exceeds the cap, the
/// application is terminated with an explanatory message rather than continuing
/// to spend tokens.
/// </summary>
public sealed class TokenCapChatClient(IChatClient innerClient, long maxTotalTokens)
    : DelegatingChatClient(innerClient)
{
    private long _totalTokens;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ChatResponse response = await base.GetResponseAsync(messages, options, cancellationToken);
        Track(response.Usage);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (ChatResponseUpdate update in
            base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (AIContent content in update.Contents)
            {
                if (content is UsageContent usageContent)
                {
                    Track(usageContent.Details);
                }
            }

            yield return update;
        }
    }

    private void Track(UsageDetails? usage)
    {
        long used = usage?.TotalTokenCount ?? 0;
        if (used == 0)
        {
            return;
        }

        long total = Interlocked.Add(ref _totalTokens, used);
        if (total > maxTotalTokens)
        {
            throw new TokenCapExceededException(total, maxTotalTokens);
        }
    }
}

/// <summary>
/// Thrown when cumulative model token usage exceeds the configured cap. Callers
/// catch this to shut down gracefully instead of continuing to spend tokens.
/// </summary>
public sealed class TokenCapExceededException(long tokensUsed, long tokenLimit)
    : Exception($"Token cap exceeded: consumed {tokensUsed} tokens, limit is {tokenLimit}.")
{
    public long TokensUsed { get; } = tokensUsed;

    public long TokenLimit { get; } = tokenLimit;
}
