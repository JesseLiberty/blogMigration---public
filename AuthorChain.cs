using Microsoft.Extensions.AI;

namespace BlogMigration;

/// <summary>Creates the author chain.</summary>
public class AuthorChain(IChatClient llm, ChatOptions chatOptions) : IAuthorChain
{
    public async Task<string> InvokeAsync(ResearchState state)
    {
        List<string> research = state.ResearchFindings;
        string researchText = research.Count > 0 ? string.Join("\n\n", research) : "No research available.";

        string prompt = Prompts.AuthorPromptTemplate
            .Replace("{main_task}", state.MainTask)
            .Replace("{research_findings}", researchText)
            .Replace("{draft}", state.Draft)
            .Replace("{review_notes}", state.ReviewNotes);

        try
        {
            ChatResponse response = await llm.GetResponseAsync(prompt, chatOptions);
            string content = response.Text;
            return !string.IsNullOrEmpty(content) ? content : "Draft in progress...";
        }
        catch (Exception e)
        {
            Console.WriteLine($"Author error: {e.Message}");
            return "Error generating draft. Please try again.";
        }
    }

    /// <summary>Author node that creates or revises draft.</summary>
    public async Task<ResearchState> AuthorNodeAsync(ResearchState state)
    {
        Console.WriteLine("\n>>>Author");

        string draft = await InvokeAsync(state);
        Console.WriteLine($"Draft created: {draft.Length} characters");

        state.Draft = draft;
        state.RevisionNumber += 1;
        return state;
    }
}
