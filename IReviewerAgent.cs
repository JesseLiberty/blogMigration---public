namespace BlogWriter;

public interface IReviewerAgent
{
    Task<string> InvokeAsync(ResearchState state, CancellationToken cancellationToken = default);

    Task<ResearchState> ReviewerNodeAsync(ResearchState state, CancellationToken cancellationToken = default);
}
