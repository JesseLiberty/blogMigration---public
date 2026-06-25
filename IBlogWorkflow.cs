namespace BlogMigration;

public interface IBlogWorkflow
{
    Task<ResearchState> RunAsync(ResearchState state);
}
