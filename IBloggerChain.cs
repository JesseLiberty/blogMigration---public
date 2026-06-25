namespace BlogMigration;

public interface IBloggerChain
{
    Task<BloggerDecision> InvokeAsync(ResearchState state);

    Task<ResearchState> BloggerNodeAsync(ResearchState state);
}
