namespace BlogWriter;

public interface IResearcherAgent
{
    Task<string> InvokeAsync(string query);

    Task<ResearchState> ResearchNodeAsync(ResearchState state);
}
