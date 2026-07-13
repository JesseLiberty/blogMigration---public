namespace BlogWriter;

public interface IReviewerAgent
{
    Task<string> InvokeAsync(ResearchState state);

    Task<ResearchState> ReviewerNodeAsync(ResearchState state);
}
