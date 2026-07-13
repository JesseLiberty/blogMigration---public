using System.Text.Json.Serialization;

namespace BlogWriter;

/// <summary>
/// Represents the blogger stage output: the next workflow step and the task
/// description for that step.
/// </summary>
public record BloggerDecision(
    [property: JsonPropertyName("next_step")] string NextStep,
    [property: JsonPropertyName("task_description")] string TaskDescription);
