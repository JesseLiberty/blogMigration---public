namespace BlogWriter;

/// <summary>State for the research workflow.</summary>
public class ResearchState
{
    /// <summary>Hard upper bound on author/review revision cycles. Guarantees the workflow terminates.</summary>
    public const int MaxRevisions = 2;

    /// <summary>The single source-of-truth marker written to <see cref="ReviewNotes"/> on approval.</summary>
    public const string ApprovedMarker = "APPROVED";

    /// <summary>Default lower bound on the target word count for the blog post.</summary>
    public const int DefaultMinWords = 1000;

    /// <summary>Default upper bound on the target word count for the blog post.</summary>
    public const int DefaultMaxWords = 2000;

    public string MainTask { get; set; } = "";

    /// <summary>Minimum target word count for the draft. Used by the author and reviewer stages.</summary>
    public int MinWords { get; set; } = DefaultMinWords;

    /// <summary>Maximum target word count for the draft. Used by the author and reviewer stages.</summary>
    public int MaxWords { get; set; } = DefaultMaxWords;
    public List<string> ResearchFindings { get; set; } = [];
    public string Draft { get; set; } = "";
    public string ReviewNotes { get; set; } = "";
    public int RevisionNumber { get; set; }
    public string NextStep { get; set; } = "";
    public string CurrentSubTask { get; set; } = "";

    /// <summary>True when the given review text contains the approval marker (case-insensitive).</summary>
    public static bool IsApproved(string? reviewNotes) =>
        !string.IsNullOrEmpty(reviewNotes) && reviewNotes.Contains(ApprovedMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True while the draft is not yet approved AND the revision cap has not been
    /// reached. Drives the bounded review loop; when false the workflow terminates.
    /// </summary>
    public bool NeedsRevision => !IsApproved(ReviewNotes) && RevisionNumber < MaxRevisions;
}
