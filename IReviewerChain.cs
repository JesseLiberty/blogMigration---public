namespace BlogMigration;

public interface IReviewerChain
{
    Task<string> InvokeAsync(ResearchState state);

    Task<ResearchState> ReviewerNodeAsync(ResearchState state);
}
