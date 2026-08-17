using BlogWriter;
using Xunit;

namespace BlogWriter.Tests;

public class ResearchStateTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Needs more detail on X.", false)]
    [InlineData("APPROVED", true)]
    [InlineData("approved - nice work", true)]
    [InlineData("APPROVED - Maximum revisions reached.", true)]
    public void IsApproved_DetectsMarkerCaseInsensitively(string? reviewNotes, bool expected)
    {
        Assert.Equal(expected, ResearchState.IsApproved(reviewNotes));
    }

    [Fact]
    public void NeedsRevision_TrueWhenNotApprovedAndUnderCap()
    {
        var state = new ResearchState { ReviewNotes = "Please revise the intro.", RevisionNumber = 1 };

        Assert.True(state.NeedsRevision);
    }

    [Fact]
    public void NeedsRevision_FalseWhenApproved()
    {
        var state = new ResearchState { ReviewNotes = ResearchState.ApprovedMarker, RevisionNumber = 1 };

        Assert.False(state.NeedsRevision);
    }

    [Fact]
    public void NeedsRevision_FalseWhenRevisionCapReached()
    {
        var state = new ResearchState { ReviewNotes = "Still needs work.", RevisionNumber = ResearchState.MaxRevisions };

        Assert.False(state.NeedsRevision);
    }

    [Fact]
    public void NeedsRevision_FalseOneStepBelowCap_TrueWhenBelow()
    {
        var belowCap = new ResearchState { ReviewNotes = "revise", RevisionNumber = ResearchState.MaxRevisions - 1 };
        var atCap = new ResearchState { ReviewNotes = "revise", RevisionNumber = ResearchState.MaxRevisions };

        Assert.True(belowCap.NeedsRevision);
        Assert.False(atCap.NeedsRevision);
    }
}
