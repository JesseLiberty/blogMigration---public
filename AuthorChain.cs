using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace BlogMigration;

/// <summary>
/// Author chain backed by a Microsoft Agent Framework <see cref="ChatClientAgent"/>.
///
/// MAF idiom change: the writing role now lives in the agent's Instructions
/// (set once), and only the volatile state (task, findings, draft, review notes)
/// is sent as the per-turn user message — replacing the previous
/// <c>string.Replace("{token}", ...)</c> templating against the raw IChatClient.
/// </summary>
public class AuthorChain : IAuthorChain
{
    private readonly ChatClientAgent _agent;

    public AuthorChain(IChatClient llm, ChatOptions chatOptions)
    {
        _agent = new ChatClientAgent(llm, new ChatClientAgentOptions
        {
            Name = "Author",
            ChatOptions = new ChatOptions
            {
                Instructions = Prompts.AuthorInstructions,
                Temperature = chatOptions.Temperature,
                MaxOutputTokens = chatOptions.MaxOutputTokens,
            },
        });
    }

    public async Task<string> InvokeAsync(ResearchState state)
    {
        List<string> research = state.ResearchFindings;
        string researchText = research.Count > 0 ? string.Join("\n\n", research) : "No research available.";

        // Per-turn input only — the role/instructions are already on the agent.
        string message = $"""
            Main Task: {state.MainTask}

            Research Findings:
            {researchText}

            Current Draft: {(string.IsNullOrEmpty(state.Draft) ? "(none — write the first draft)" : state.Draft)}

            Review Notes: {(string.IsNullOrEmpty(state.ReviewNotes) ? "(none)" : state.ReviewNotes)}
            """;

        try
        {
            AgentResponse response = await _agent.RunAsync(message);
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
