using Microsoft.Agents.AI.Workflows;

namespace BlogMigration;

/// <summary>
/// Blog creation workflow built on the Microsoft Agent Framework workflow engine.
///
/// Topology (faithful to the original LangGraph StateGraph):
///   Blogger → Researcher → Author → Reviewer
///   Reviewer ⇄ Author  (bounded revision loop)
///   Reviewer → Output  (on approval or revision cap)
///
/// The revision loop is bounded by <see cref="ResearchState.MaxRevisions"/>: the
/// loop-back edge only fires while the draft is unapproved AND the revision count
/// is below the cap, so the workflow is guaranteed to terminate even if the
/// reviewer never returns "APPROVED".
/// </summary>
public class BlogWorkflow(
    IBloggerChain blogger,
    IResearcherAgent researcher,
    IAuthorChain author,
    IReviewerChain reviewer) : IBlogWorkflow
{
    public async Task<ResearchState> RunAsync(ResearchState state)
    {
        var bloggerExecutor = new BloggerExecutor(blogger);
        var researcherExecutor = new ResearcherExecutor(researcher);
        var authorExecutor = new AuthorExecutor(author);
        var reviewerExecutor = new ReviewerExecutor(reviewer);

        Workflow workflow = new WorkflowBuilder(bloggerExecutor)
            .AddEdge(bloggerExecutor, researcherExecutor)
            .AddEdge(researcherExecutor, authorExecutor)
            .AddEdge(authorExecutor, reviewerExecutor)
            // Bounded revision loop: route back to the author only while the draft
            // still needs work and the revision cap has not been reached. When the
            // condition is false the reviewer instead yields the final output.
            .AddEdge<ResearchState>(reviewerExecutor, authorExecutor, condition: s => s?.NeedsRevision == true)
            .WithOutputFrom(reviewerExecutor)
            .Build();

        // Stream execution instead of running to completion in one shot. The
        // topology is identical to before (proven terminating, MAF-Doctor grade A);
        // streaming simply surfaces each executor's lifecycle as it happens, giving
        // live progress and replacing the scattered Console.WriteLine tracing that
        // previously lived inside the node classes. The final ResearchState is
        // captured from the WorkflowOutputEvent emitted by the reviewer.
        StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, state);

        ResearchState? result = null;

        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case ExecutorInvokedEvent invoked:
                    Console.WriteLine($"[workflow] → {invoked.ExecutorId} started");
                    break;

                case ExecutorCompletedEvent completed:
                    Console.WriteLine($"[workflow] ✓ {completed.ExecutorId} completed");
                    break;

                case ExecutorFailedEvent failed:
                    Console.WriteLine($"[workflow] ✗ {failed.ExecutorId} failed: {(failed.Data as Exception)?.Message}");
                    break;

                case WorkflowOutputEvent { Data: ResearchState finalState }:
                    // The reviewer yielded the final, approved (or revision-capped) state.
                    result = finalState;
                    break;
            }
        }

        // Fall back to the input state only if no output event was ever produced.
        return result ?? state;
    }
}
