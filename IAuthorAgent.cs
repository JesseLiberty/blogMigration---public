namespace BlogWriter;

public interface IAuthorAgent
{
    Task<string> InvokeAsync(ResearchState state, CancellationToken cancellationToken = default);

    Task<ResearchState> AuthorNodeAsync(ResearchState state, CancellationToken cancellationToken = default);
}
