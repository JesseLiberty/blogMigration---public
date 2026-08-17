using Microsoft.Agents.AI.Workflows;

namespace BlogWriter;

// MAF workflow executors. Each wraps one of the existing "node" chains so the
// tested business logic is reused unchanged. A [MessageHandler] returning
// ValueTask<ResearchState> auto-routes the returned state to downstream edges.

/// <summary>Entry executor: lets the blogger plan the task and seed the sub-task.</summary>
internal sealed partial class BloggerExecutor(IBloggerAgent blogger) : Executor("Blogger")
{
    [MessageHandler]
    private async ValueTask<ResearchState> HandleAsync(ResearchState state, IWorkflowContext context, CancellationToken cancellationToken)
        => await blogger.BloggerNodeAsync(state, cancellationToken);
}

/// <summary>Gathers research findings.</summary>
internal sealed partial class ResearcherExecutor(IResearcherAgent researcher) : Executor("Researcher")
{
    [MessageHandler]
    private async ValueTask<ResearchState> HandleAsync(ResearchState state, IWorkflowContext context, CancellationToken cancellationToken)
        => await researcher.ResearchNodeAsync(state, cancellationToken);
}

/// <summary>Writes or revises the draft (increments the revision counter).</summary>
internal sealed partial class AuthorExecutor(IAuthorAgent author) : Executor("Author")
{
    [MessageHandler]
    private async ValueTask<ResearchState> HandleAsync(ResearchState state, IWorkflowContext context, CancellationToken cancellationToken)
        => await author.AuthorNodeAsync(state, cancellationToken);
}

/// <summary>
/// Reviews the draft and records approval / revision notes. Acts as the terminal
/// output node: when no further revision is needed it yields the final state.
/// </summary>
internal sealed partial class ReviewerExecutor(IReviewerAgent reviewer) : Executor("Reviewer")
{
    [MessageHandler]
    private async ValueTask<ResearchState> HandleAsync(ResearchState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        state = await reviewer.ReviewerNodeAsync(state, cancellationToken);

        if (!state.NeedsRevision)
        {
            // Approved, or the revision cap was hit — emit the final result.
            await context.YieldOutputAsync(state);
        }

        // Returned state is routed back to the author only when the loop edge
        // condition (NeedsRevision) is satisfied; otherwise it goes nowhere.
        return state;
    }
}
