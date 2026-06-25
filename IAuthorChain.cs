namespace BlogMigration;

public interface IAuthorChain
{
    Task<string> InvokeAsync(ResearchState state);

    Task<ResearchState> AuthorNodeAsync(ResearchState state);
}
