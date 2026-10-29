using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using Xunit;

namespace InsuranceClaimsTracker.UnitTests.Domain;

public class ClaimTests
{
    [Fact]
    public void Constructor_WithValidParameters_InitializesClaimAsSubmitted()
    {
        // Arrange
        var incidentDate = DateTimeOffset.UtcNow.AddDays(-2);

        // Act
        var claim = new Claim(101, 1500m, incidentDate, "Rear bumper collision damage.");

        // Assert
        Assert.Equal(101, claim.PolicyId);
        Assert.Equal(1500m, claim.ClaimAmount);
        Assert.Equal(incidentDate, claim.IncidentDate);
        Assert.Equal("Rear bumper collision damage.", claim.Description);
        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.True(claim.CreatedAt <= DateTimeOffset.UtcNow);
        Assert.Null(claim.UpdatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Constructor_WithInvalidAmount_ThrowsArgumentOutOfRangeException(decimal invalidAmount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Claim(101, invalidAmount, DateTimeOffset.UtcNow, "Valid description"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_WithInvalidDescription_ThrowsArgumentException(string? invalidDescription)
    {
        Assert.Throws<ArgumentException>(() =>
            new Claim(101, 500m, DateTimeOffset.UtcNow, invalidDescription!));
    }

    [Theory]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.UnderReview, true)]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.Approved, false)]
    [InlineData(ClaimStatus.Submitted, ClaimStatus.Paid, false)]
    [InlineData(ClaimStatus.UnderReview, ClaimStatus.Approved, true)]
    [InlineData(ClaimStatus.UnderReview, ClaimStatus.Rejected, true)]
    [InlineData(ClaimStatus.UnderReview, ClaimStatus.Paid, false)]
    [InlineData(ClaimStatus.Approved, ClaimStatus.Paid, true)]
    [InlineData(ClaimStatus.Approved, ClaimStatus.Rejected, false)]
    [InlineData(ClaimStatus.Rejected, ClaimStatus.Approved, false)]
    [InlineData(ClaimStatus.Paid, ClaimStatus.Approved, false)]
    public void CanTransitionTo_ValidatesStateLifecycle(ClaimStatus current, ClaimStatus target, bool expected)
    {
        var claim = new Claim(1, 100m, DateTimeOffset.UtcNow, "Test claim")
        {
            Status = current
        };

        Assert.Equal(expected, claim.CanTransitionTo(target));
    }
}
