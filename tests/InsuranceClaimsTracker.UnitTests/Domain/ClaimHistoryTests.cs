using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using Xunit;

namespace InsuranceClaimsTracker.UnitTests.Domain;

public class ClaimHistoryTests
{
    [Fact]
    public void Constructor_WithValidParameters_InitializesAuditEntry()
    {
        // Act
        var history = new ClaimHistory(501, ClaimStatus.Submitted, ClaimStatus.UnderReview, 99, "Started adjuster review.");

        // Assert
        Assert.Equal(501, history.ClaimId);
        Assert.Equal(ClaimStatus.Submitted, history.PreviousStatus);
        Assert.Equal(ClaimStatus.UnderReview, history.NewStatus);
        Assert.Equal(99, history.ChangedById);
        Assert.Equal("Started adjuster review.", history.ReviewNote);
        Assert.True(history.ChangedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Constructor_WithInitialSubmission_AllowsNullPreviousStatus()
    {
        // Act
        var history = new ClaimHistory(501, null, ClaimStatus.Submitted, 1, null);

        // Assert
        Assert.Null(history.PreviousStatus);
        Assert.Equal(ClaimStatus.Submitted, history.NewStatus);
        Assert.Null(history.ReviewNote);
    }
}
