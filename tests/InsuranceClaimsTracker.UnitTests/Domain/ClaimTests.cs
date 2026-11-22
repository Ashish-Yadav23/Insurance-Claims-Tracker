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

    [Fact]
    public void StartReview_FromSubmitted_TransitionsToUnderReviewAndAddsHistory()
    {
        var claim = new Claim(1, 1500m, DateTimeOffset.UtcNow.AddDays(-1), "Collision damage");
        const int adminId = 99;

        claim.StartReview(adminId, "Initiating claim assessment.");

        Assert.Equal(ClaimStatus.UnderReview, claim.Status);
        Assert.NotNull(claim.UpdatedAt);
        Assert.Single(claim.History);

        var history = claim.History.First();
        Assert.Equal(ClaimStatus.Submitted, history.PreviousStatus);
        Assert.Equal(ClaimStatus.UnderReview, history.NewStatus);
        Assert.Equal(adminId, history.ChangedById);
        Assert.Equal("Initiating claim assessment.", history.ReviewNote);
        Assert.True(history.ChangedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void StartReview_WithDefaultNote_UsesDefaultNote()
    {
        var claim = new Claim(1, 1500m, DateTimeOffset.UtcNow.AddDays(-1), "Collision damage");

        claim.StartReview(99, null);

        Assert.Equal(ClaimStatus.UnderReview, claim.Status);
        Assert.Equal("Review initiated by administrator.", claim.History.First().ReviewNote);
    }

    [Theory]
    [InlineData(ClaimStatus.UnderReview)]
    [InlineData(ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.Paid)]
    public void StartReview_WhenNotSubmitted_ThrowsInvalidClaimStateException(ClaimStatus status)
    {
        var claim = new Claim(1, 1000m, DateTimeOffset.UtcNow, "Damage") { Status = status };

        var ex = Assert.Throws<InsuranceClaimsTracker.Domain.Exceptions.InvalidClaimStateException>(() =>
            claim.StartReview(99, "Note"));

        Assert.Contains("Cannot start review on a claim with status", ex.Message);
    }

    [Fact]
    public void Approve_FromUnderReview_TransitionsToApprovedAndRecordsAmountAndHistory()
    {
        var claim = new Claim(1, 2000m, DateTimeOffset.UtcNow.AddDays(-2), "Water damage");
        claim.StartReview(99);

        claim.Approve(99, 1800m, "Coverage confirmed and repair estimate verified.");

        Assert.Equal(ClaimStatus.Approved, claim.Status);
        Assert.Equal(1800m, claim.ApprovedAmount);
        Assert.Equal(2, claim.History.Count);

        var approvalHistory = claim.History.Last();
        Assert.Equal(ClaimStatus.UnderReview, approvalHistory.PreviousStatus);
        Assert.Equal(ClaimStatus.Approved, approvalHistory.NewStatus);
        Assert.Equal(99, approvalHistory.ChangedById);
        Assert.Equal("Coverage confirmed and repair estimate verified.", approvalHistory.ReviewNote);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void Approve_WithZeroOrNegativeAmount_ThrowsArgumentOutOfRangeException(decimal invalidAmount)
    {
        var claim = new Claim(1, 2000m, DateTimeOffset.UtcNow, "Damage");
        claim.StartReview(99);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            claim.Approve(99, invalidAmount, "Note"));
    }

    [Fact]
    public void Approve_WithAmountExceedingClaimAmount_ThrowsArgumentOutOfRangeException()
    {
        var claim = new Claim(1, 2000m, DateTimeOffset.UtcNow, "Damage");
        claim.StartReview(99);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            claim.Approve(99, 2500m, "Note"));

        Assert.Contains("cannot exceed the claimed amount", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Approve_WithInvalidReviewNote_ThrowsArgumentException(string? invalidNote)
    {
        var claim = new Claim(1, 2000m, DateTimeOffset.UtcNow, "Damage");
        claim.StartReview(99);

        Assert.Throws<ArgumentException>(() =>
            claim.Approve(99, 1500m, invalidNote!));
    }

    [Theory]
    [InlineData(ClaimStatus.Submitted)]
    [InlineData(ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.Paid)]
    public void Approve_WhenNotUnderReview_ThrowsInvalidClaimStateException(ClaimStatus status)
    {
        var claim = new Claim(1, 2000m, DateTimeOffset.UtcNow, "Damage") { Status = status };

        Assert.Throws<InsuranceClaimsTracker.Domain.Exceptions.InvalidClaimStateException>(() =>
            claim.Approve(99, 1000m, "Valid note"));
    }

    [Fact]
    public void Reject_FromUnderReview_TransitionsToRejectedAndAddsHistory()
    {
        var claim = new Claim(1, 3000m, DateTimeOffset.UtcNow.AddDays(-1), "Theft damage");
        claim.StartReview(99);

        claim.Reject(99, "Incident falls outside the insured perils.");

        Assert.Equal(ClaimStatus.Rejected, claim.Status);
        Assert.Null(claim.ApprovedAmount);
        Assert.Equal(2, claim.History.Count);

        var rejectionHistory = claim.History.Last();
        Assert.Equal(ClaimStatus.UnderReview, rejectionHistory.PreviousStatus);
        Assert.Equal(ClaimStatus.Rejected, rejectionHistory.NewStatus);
        Assert.Equal(99, rejectionHistory.ChangedById);
        Assert.Equal("Incident falls outside the insured perils.", rejectionHistory.ReviewNote);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Reject_WithInvalidReviewNote_ThrowsArgumentException(string? invalidNote)
    {
        var claim = new Claim(1, 3000m, DateTimeOffset.UtcNow, "Damage");
        claim.StartReview(99);

        Assert.Throws<ArgumentException>(() =>
            claim.Reject(99, invalidNote!));
    }

    [Theory]
    [InlineData(ClaimStatus.Submitted)]
    [InlineData(ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.Paid)]
    public void Reject_WhenNotUnderReview_ThrowsInvalidClaimStateException(ClaimStatus status)
    {
        var claim = new Claim(1, 3000m, DateTimeOffset.UtcNow, "Damage") { Status = status };

        Assert.Throws<InsuranceClaimsTracker.Domain.Exceptions.InvalidClaimStateException>(() =>
            claim.Reject(99, "Valid note"));
    }

    [Fact]
    public void RecordPayout_FromApproved_TransitionsToPaidAndRecordsDetails()
    {
        var claim = new Claim(1, 5000m, DateTimeOffset.UtcNow.AddDays(-3), "Hail damage");
        claim.StartReview(99);
        claim.Approve(99, 4500m, "Approved after inspection");

        var paidTime = DateTimeOffset.UtcNow;
        claim.RecordPayout(99, "TXN-PAYOUT-987654", paidTime);

        Assert.Equal(ClaimStatus.Paid, claim.Status);
        Assert.Equal("TXN-PAYOUT-987654", claim.PaymentReference);
        Assert.Equal(paidTime, claim.PaidAt);
        Assert.Equal(3, claim.History.Count);

        var payoutHistory = claim.History.Last();
        Assert.Equal(ClaimStatus.Approved, payoutHistory.PreviousStatus);
        Assert.Equal(ClaimStatus.Paid, payoutHistory.NewStatus);
        Assert.Equal(99, payoutHistory.ChangedById);
        Assert.Equal("Payment reference: TXN-PAYOUT-987654", payoutHistory.ReviewNote);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RecordPayout_WithInvalidPaymentReference_ThrowsArgumentException(string? invalidRef)
    {
        var claim = new Claim(1, 5000m, DateTimeOffset.UtcNow, "Damage") { Status = ClaimStatus.Approved };

        Assert.Throws<ArgumentException>(() =>
            claim.RecordPayout(99, invalidRef!));
    }

    [Fact]
    public void RecordPayout_WhenAlreadyPaid_ThrowsDuplicatePayoutInvalidClaimStateException()
    {
        var claim = new Claim(1, 5000m, DateTimeOffset.UtcNow, "Damage");
        claim.StartReview(99);
        claim.Approve(99, 4000m, "Approved");
        claim.RecordPayout(99, "TXN-001");

        var ex = Assert.Throws<InsuranceClaimsTracker.Domain.Exceptions.InvalidClaimStateException>(() =>
            claim.RecordPayout(99, "TXN-002"));

        Assert.Contains("Duplicate payout attempt", ex.Message);
    }

    [Theory]
    [InlineData(ClaimStatus.Submitted)]
    [InlineData(ClaimStatus.UnderReview)]
    [InlineData(ClaimStatus.Rejected)]
    public void RecordPayout_WhenNotApproved_ThrowsInvalidClaimStateException(ClaimStatus status)
    {
        var claim = new Claim(1, 5000m, DateTimeOffset.UtcNow, "Damage") { Status = status };

        Assert.Throws<InsuranceClaimsTracker.Domain.Exceptions.InvalidClaimStateException>(() =>
            claim.RecordPayout(99, "TXN-001"));
    }
}
