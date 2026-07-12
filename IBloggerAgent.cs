namespace BlogMigration;

public interface IBloggerAgent
{
    Task<BloggerDecision> InvokeAsync(ResearchState state);

    Task<ResearchState> BloggerNodeAsync(ResearchState state);
}
