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

        Run run = await InProcessExecution.RunAsync(workflow, state);

        foreach (WorkflowEvent evt in run.NewEvents)
        {
            if (evt is WorkflowOutputEvent { Data: ResearchState result })
            {
                return result;
            }
        }

        return state;
    }
}
