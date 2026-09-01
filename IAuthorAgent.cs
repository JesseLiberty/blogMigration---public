namespace BlogWriter;

public interface IAuthorAgent
{
    /// <summary>Returns the generated draft, or null if the agent produced no usable content.</summary>
    Task<string?> InvokeAsync(ResearchState state, CancellationToken cancellationToken = default);

    Task<ResearchState> AuthorNodeAsync(ResearchState state, CancellationToken cancellationToken = default);
}
