namespace BlogWriter;

public interface IAuthorAgent
{
    Task<string> InvokeAsync(ResearchState state);

    Task<ResearchState> AuthorNodeAsync(ResearchState state);
}
