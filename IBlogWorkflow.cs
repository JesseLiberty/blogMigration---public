namespace BlogWriter;

public interface IBlogWorkflow
{
    Task<ResearchState> RunAsync(ResearchState state);
}
