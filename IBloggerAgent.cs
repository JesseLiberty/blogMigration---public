namespace BlogWriter;

public interface IBloggerAgent
{
    Task<BloggerDecision> InvokeAsync(ResearchState state, CancellationToken cancellationToken = default);

    Task<ResearchState> BloggerNodeAsync(ResearchState state, CancellationToken cancellationToken = default);
}
