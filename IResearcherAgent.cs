namespace BlogWriter;

public interface IResearcherAgent
{
    Task<string> InvokeAsync(string query, CancellationToken cancellationToken = default);

    Task<ResearchState> ResearchNodeAsync(ResearchState state, CancellationToken cancellationToken = default);
}
