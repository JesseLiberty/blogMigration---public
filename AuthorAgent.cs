using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace BlogWriter;

/// <summary>
/// Generates and revises blog drafts using a <see cref="ChatClientAgent"/>.
///
/// The agent receives the main task, research findings, current draft, and
/// review notes, then returns draft content for the author stage.
/// </summary>
public class AuthorAgent : IAuthorAgent
{
    private readonly ChatClientAgent _agent;

    // Emits a span per draft creation/revision. Activated by the ActivityListener
    // registered in Program.cs (or an OpenTelemetry TracerProvider).
    private static readonly ActivitySource s_activitySource = new("BlogWriter.AuthorAgent");

    private readonly ILogger<AuthorAgent> _logger;

    public AuthorAgent(IChatClient llm, ChatOptions chatOptions, ILogger<AuthorAgent> logger)
    {
        _logger = logger;

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
        _logger.LogInformation("AuthorAgent initialized.");
    }

    public async Task<string> InvokeAsync(ResearchState state)
    {
        using Activity? activity = s_activitySource.StartActivity("Author.Invoke");
        activity?.SetTag("blog.revision", state.RevisionNumber);

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
