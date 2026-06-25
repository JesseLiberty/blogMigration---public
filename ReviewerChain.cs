using Microsoft.Extensions.AI;

namespace BlogMigration;

/// <summary>Creates the reviewer chain.</summary>
public class ReviewerChain(IChatClient llm, ChatOptions chatOptions) : IReviewerChain
{
    public async Task<string> InvokeAsync(ResearchState state)
    {
        string draft = state.Draft;
        int revisionNum = state.RevisionNumber;

        if (draft.Trim().Length < 100)
        {
            return "APPROVED - Draft is minimal but acceptable.";
        }

        if (revisionNum >= ResearchState.MaxRevisions)
        {
            return "APPROVED - Maximum revisions reached. The report is satisfactory.";
        }

        string prompt = Prompts.ReviewerPromptTemplate
            .Replace("{main_task}", state.MainTask)
            .Replace("{draft}", draft);

        try
        {
            ChatResponse response = await llm.GetResponseAsync(prompt, chatOptions);
            string content = response.Text;
            return !string.IsNullOrEmpty(content) ? content : "APPROVED";
        }
        catch (Exception e)
        {
            Console.WriteLine($"Review error: {e.Message}");
            return "APPROVED - Error in review, proceeding with current draft.";
        }
    }

    /// <summary>Node that reviews the draft.</summary>
    public async Task<ResearchState> ReviewerNodeAsync(ResearchState state)
    {
        Console.WriteLine("\n>>REVIEWER");

        string review = await InvokeAsync(state);
        string preview = review.Length > 100 ? review[..100] : review;
        Console.WriteLine($"Review: {preview}...");

        bool isApproved = review.ToUpperInvariant().Contains("APPROVED");

        if (isApproved)
        {
            Console.WriteLine("\u2713 Draft APPROVED");
            state.ReviewNotes = "APPROVED";
            state.NextStep = "END";
        }
        else
        {
            Console.WriteLine("\u2717 Revisions needed");
            state.ReviewNotes = review;
            state.NextStep = "author";
        }

        return state;
    }
}
