using Microsoft.Extensions.AI;

namespace BlogWriter.Tests;

/// <summary>Minimal <see cref="IChatClient"/> test double returning a canned response with fixed usage.</summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly Func<UsageDetails?> _usageFactory;

    public FakeChatClient(Func<UsageDetails?> usageFactory) => _usageFactory = usageFactory;

    public FakeChatClient(long totalTokens) : this(() => new UsageDetails { TotalTokenCount = totalTokens })
    {
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
        {
            Usage = _usageFactory(),
        };
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>An <see cref="IChatClient"/> test double that always returns an empty response, simulating a model that produced no content.</summary>
internal sealed class EmptyChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))
        {
            Usage = new UsageDetails { TotalTokenCount = 10 },
        };
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, "");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>An <see cref="IChatClient"/> that fails the test if it is ever invoked.</summary>
internal sealed class ThrowingChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The model should not have been called for this deterministic routing path.");

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The model should not have been called for this deterministic routing path.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
