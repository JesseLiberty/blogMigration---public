using System.Text.Json.Serialization;

namespace BlogMigration;

public record BloggerDecision(
    [property: JsonPropertyName("next_step")] string NextStep,
    [property: JsonPropertyName("task_description")] string TaskDescription);
