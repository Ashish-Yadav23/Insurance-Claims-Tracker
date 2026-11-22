using InsuranceClaimsTracker.Application.DTOs.Admin;
using InsuranceClaimsTracker.Application.Services;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using InsuranceClaimsTracker.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InsuranceClaimsTracker.UnitTests.Application;

public class AdminClaimServiceTests
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TestCurrentUserService _currentUserService;
    private readonly AdminClaimService _adminClaimService;

    public AdminClaimServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(dbOptions);
        _currentUserService = new TestCurrentUserService
        {
            UserId = 10,
            UserRole = "Admin",
            IsAuthenticated = true
        };

        _adminClaimService = new AdminClaimService(_dbContext, _currentUserService);
    }

    private async Task<(User customer, Policy policy, Claim claim)> SeedClaimAsync(
        ClaimStatus status = ClaimStatus.Submitted,
        decimal claimAmount = 2500m,
        byte[]? rowVersion = null)
    {
        var customer = new User("John Doe", $"john.{Guid.NewGuid()}@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var policy = new Policy($"POL-{Guid.NewGuid():N}", customer.Id, PolicyType.Auto, 10000m,
            DateTimeOffset.UtcNow.AddMonths(-3), DateTimeOffset.UtcNow.AddMonths(9));
        _dbContext.Policies.Add(policy);
        await _dbContext.SaveChangesAsync();

        var claim = new Claim(policy.Id, claimAmount, DateTimeOffset.UtcNow.AddDays(-5), "Front fender collision damage.")
        {
            Status = status,
            RowVersion = rowVersion ?? [0x01, 0x02, 0x03, 0x04]
        };

        if (status == ClaimStatus.Approved)
        {
            claim.ApprovedAmount = claimAmount * 0.9m;
        }

        _dbContext.Claims.Add(claim);
        await _dbContext.SaveChangesAsync();

        return (customer, policy, claim);
    }

    #region StartReviewAsync Tests

    [Fact]
    public async Task StartReviewAsync_WhenUserNotAdmin_ReturnsForbidden403()
    {
        _currentUserService.UserRole = "Customer";

        var result = await _adminClaimService.StartReviewAsync(1, new StartReviewRequestDto
        {
            RowVersion = Convert.ToBase64String([1, 2, 3])
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task StartReviewAsync_WhenUserNotAuthenticated_ReturnsUnauthorized401()
    {
        _currentUserService.IsAuthenticated = false;

        var result = await _adminClaimService.StartReviewAsync(1, new StartReviewRequestDto
        {
            RowVersion = Convert.ToBase64String([1, 2, 3])
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task StartReviewAsync_WithInvalidRowVersionFormat_ReturnsBadRequest400()
    {
        var result = await _adminClaimService.StartReviewAsync(1, new StartReviewRequestDto
        {
            RowVersion = "not-base-64!"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("Invalid RowVersion format", result.ErrorMessage);
    }

    [Fact]
    public async Task StartReviewAsync_WhenClaimNotFound_ReturnsNotFound404()
    {
        var result = await _adminClaimService.StartReviewAsync(9999, new StartReviewRequestDto
        {
            RowVersion = Convert.ToBase64String([1, 2, 3])
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("Claim not found.", result.ErrorMessage);
    }

    [Fact]
    public async Task StartReviewAsync_WithMismatchedRowVersion_ReturnsConflict409()
    {
        var (_, _, claim) = await SeedClaimAsync(ClaimStatus.Submitted, rowVersion: [1, 2, 3, 4]);

        var result = await _adminClaimService.StartReviewAsync(claim.Id, new StartReviewRequestDto
        {
            RowVersion = Convert.ToBase64String([9, 9, 9, 9])
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(AdminClaimService.ConcurrencyConflictMessage, result.ErrorMessage);
    }

    [Fact]
    public async Task StartReviewAsync_WhenClaimNotSubmitted_ReturnsBadRequest400()
    {
        var (_, _, claim) = await SeedClaimAsync(ClaimStatus.UnderReview, rowVersion: [1, 2, 3, 4]);

        var result = await _adminClaimService.StartReviewAsync(claim.Id, new StartReviewRequestDto
        {
            RowVersion = Convert.ToBase64String([1, 2, 3, 4])
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("Cannot start review on a claim with status", result.ErrorMessage);
    }

    [Fact]
    public async Task StartReviewAsync_WithValidRequest_TransitionsToUnderReviewAndRecordsHistory()
    {
        var token = new byte[] { 1, 2, 3, 4 };
        var (_, _, claim) = await SeedClaimAsync(ClaimStatus.Submitted, rowVersion: token);

        var result = await _adminClaimService.StartReviewAsync(claim.Id, new StartReviewRequestDto
        {
            RowVersion = Convert.ToBase64String(token),
            ReviewNote = "Starting detailed review."
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(ClaimStatus.UnderReview.ToString(), result.Value!.Status);

        var updatedClaim = await _dbContext.Claims.Include(c => c.History).FirstAsync(c => c.Id == claim.Id);
        Assert.Equal(ClaimStatus.UnderReview, updatedClaim.Status);
        Assert.Contains(updatedClaim.History, h => h.NewStatus == ClaimStatus.UnderReview && h.ChangedById == 10);
    }

    #endregion

    #region ProcessDecisionAsync Tests

    [Fact]
    public async Task ProcessDecisionAsync_WhenUserNotAdmin_ReturnsForbidden403()
    {
        _currentUserService.UserRole = "Customer";

        var result = await _adminClaimService.ProcessDecisionAsync(1, new ClaimDecisionRequestDto
        {
            Decision = "Approved",
            ApprovedAmount = 500m,
            ReviewNote = "Approved",
            RowVersion = Convert.ToBase64String([1, 2, 3])
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task ProcessDecisionAsync_WithMissingDecision_ReturnsBadRequest400()
    {
        var result = await _adminClaimService.ProcessDecisionAsync(1, new ClaimDecisionRequestDto
        {
            Decision = "",
            ReviewNote = "Some note",
            RowVersion = Convert.ToBase64String([1, 2, 3])
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("Decision is required", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessDecisionAsync_WithMissingReviewNote_ReturnsBadRequest400()
    {
        var result = await _adminClaimService.ProcessDecisionAsync(1, new ClaimDecisionRequestDto
        {
            Decision = "Approved",
            ApprovedAmount = 1000m,
            ReviewNote = "  ",
            RowVersion = Convert.ToBase64String([1, 2, 3])
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("A non-empty review note is required", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessDecisionAsync_WithInvalidDecisionKeyword_ReturnsBadRequest400()
    {
        var token = new byte[] { 1, 2, 3, 4 };
        var (_, _, claim) = await SeedClaimAsync(ClaimStatus.UnderReview, rowVersion: token);

        var result = await _adminClaimService.ProcessDecisionAsync(claim.Id, new ClaimDecisionRequestDto
        {
            Decision = "Hold",
            ReviewNote = "Holding claim",
            RowVersion = Convert.ToBase64String(token)
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("Decision must be either 'Approved' or 'Rejected'", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessDecisionAsync_WhenApproveWithoutAmount_ReturnsBadRequest400()
    {
        var token = new byte[] { 1, 2, 3, 4 };
        var (_, _, claim) = await SeedClaimAsync(ClaimStatus.UnderReview, rowVersion: token);

        var result = await _adminClaimService.ProcessDecisionAsync(claim.Id, new ClaimDecisionRequestDto
        {
            Decision = "Approved",
            ApprovedAmount = null,
            ReviewNote = "Approved without amount",
            RowVersion = Convert.ToBase64String(token)
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("Approved amount is required when approving a claim", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessDecisionAsync_WhenRejectWithAmount_ReturnsBadRequest400()
    {
        var token = new byte[] { 1, 2, 3, 4 };
        var (_, _, claim) = await SeedClaimAsync(ClaimStatus.UnderReview, rowVersion: token);

        var result = await _adminClaimService.ProcessDecisionAsync(claim.Id, new ClaimDecisionRequestDto
        {
            Decision = "Rejected",
            ApprovedAmount = 500m,
            ReviewNote = "Rejected note",
            RowVersion = Convert.ToBase64String(token)
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("Approved amount must not be provided for rejected claims", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessDecisionAsync_WithMismatchedRowVersion_ReturnsConflict409()
    {
        var (_, _, claim) = await SeedClaimAsync(ClaimStatus.UnderReview, rowVersion: [1, 2, 3, 4]);

        var result = await _adminClaimService.ProcessDecisionAsync(claim.Id, new ClaimDecisionRequestDto
        {
            Decision = "Approved",
            ApprovedAmount = 1000m,
            ReviewNote = "Approved note",
            RowVersion = Convert.ToBase64String([9, 9, 9, 9])
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(AdminClaimService.ConcurrencyConflictMessage, result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessDecisionAsync_ApproveValidRequest_TransitionsToApprovedAndSaves()
    {
        var token = new byte[] { 5, 6, 7, 8 };
        var (_, _, claim) = await SeedClaimAsync(ClaimStatus.UnderReview, claimAmount: 3000m, rowVersion: token);

        var result = await _adminClaimService.ProcessDecisionAsync(claim.Id, new ClaimDecisionRequestDto
        {
            Decision = "Approved",
            ApprovedAmount = 2700m,
            ReviewNote = "Full inspection report verified and coverage confirmed.",
            RowVersion = Convert.ToBase64String(token)
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("Approved", result.Value!.Status);
        Assert.Equal(2700m, result.Value.ApprovedAmount);

        var updatedClaim = await _dbContext.Claims.Include(c => c.History).FirstAsync(c => c.Id == claim.Id);
        Assert.Equal(ClaimStatus.Approved, updatedClaim.Status);
        Assert.Equal(2700m, updatedClaim.ApprovedAmount);
        Assert.Contains(updatedClaim.History, h => h.NewStatus == ClaimStatus.Approved && h.ChangedById == 10);
    }

    [Fact]
    public async Task ProcessDecisionAsync_RejectValidRequest_TransitionsToRejectedAndClearsAmount()
    {
        var token = new byte[] { 5, 6, 7, 8 };
        var (_, _, claim) = await SeedClaimAsync(ClaimStatus.UnderReview, claimAmount: 3000m, rowVersion: token);

        var result = await _adminClaimService.ProcessDecisionAsync(claim.Id, new ClaimDecisionRequestDto
        {
            Decision = "Rejected",
            ReviewNote = "Loss occurred outside policy period.",
            RowVersion = Convert.ToBase64String(token)
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("Rejected", result.Value!.Status);
        Assert.Null(result.Value.ApprovedAmount);

        var updatedClaim = await _dbContext.Claims.Include(c => c.History).FirstAsync(c => c.Id == claim.Id);
        Assert.Equal(ClaimStatus.Rejected, updatedClaim.Status);
        Assert.Null(updatedClaim.ApprovedAmount);
        Assert.Contains(updatedClaim.History, h => h.NewStatus == ClaimStatus.Rejected && h.ChangedById == 10);
    }

    #endregion

    #region RecordPayoutAsync Tests

    [Fact]
    public async Task RecordPayoutAsync_WhenUserNotAdmin_ReturnsForbidden403()
    {
        _currentUserService.UserRole = "Customer";

        var result = await _adminClaimService.RecordPayoutAsync(1, new ClaimPayoutRequestDto
        {
            PaymentReference = "PAY-123",
            RowVersion = Convert.ToBase64String([1, 2, 3])
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task RecordPayoutAsync_WithMissingPaymentReference_ReturnsBadRequest400()
    {
        var result = await _adminClaimService.RecordPayoutAsync(1, new ClaimPayoutRequestDto
        {
            PaymentReference = "",
            RowVersion = Convert.ToBase64String([1, 2, 3])
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("Payment reference is required", result.ErrorMessage);
    }

    [Fact]
    public async Task RecordPayoutAsync_WhenClaimNotApproved_ReturnsBadRequest400()
    {
        var token = new byte[] { 1, 2, 3, 4 };
        var (_, _, claim) = await SeedClaimAsync(ClaimStatus.UnderReview, rowVersion: token);

        var result = await _adminClaimService.RecordPayoutAsync(claim.Id, new ClaimPayoutRequestDto
        {
            PaymentReference = "PAY-123456",
            RowVersion = Convert.ToBase64String(token)
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("Only claims in 'Approved' status can be marked as paid", result.ErrorMessage);
    }

    [Fact]
    public async Task RecordPayoutAsync_WhenClaimAlreadyPaid_ReturnsBadRequest400()
    {
        var token = new byte[] { 1, 2, 3, 4 };
        var (_, _, claim) = await SeedClaimAsync(ClaimStatus.Paid, rowVersion: token);

        var result = await _adminClaimService.RecordPayoutAsync(claim.Id, new ClaimPayoutRequestDto
        {
            PaymentReference = "PAY-123456",
            RowVersion = Convert.ToBase64String(token)
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("Duplicate payout attempt", result.ErrorMessage);
    }

    [Fact]
    public async Task RecordPayoutAsync_WithMismatchedRowVersion_ReturnsConflict409()
    {
        var (_, _, claim) = await SeedClaimAsync(ClaimStatus.Approved, rowVersion: [1, 2, 3, 4]);

        var result = await _adminClaimService.RecordPayoutAsync(claim.Id, new ClaimPayoutRequestDto
        {
            PaymentReference = "PAY-123456",
            RowVersion = Convert.ToBase64String([9, 9, 9, 9])
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(AdminClaimService.ConcurrencyConflictMessage, result.ErrorMessage);
    }

    [Fact]
    public async Task RecordPayoutAsync_WithValidRequest_TransitionsToPaidAndRecordsPayout()
    {
        var token = new byte[] { 1, 2, 3, 4 };
        var (_, _, claim) = await SeedClaimAsync(ClaimStatus.Approved, claimAmount: 2000m, rowVersion: token);

        var result = await _adminClaimService.RecordPayoutAsync(claim.Id, new ClaimPayoutRequestDto
        {
            PaymentReference = "ACH-TRF-2026-99901",
            RowVersion = Convert.ToBase64String(token)
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("Paid", result.Value!.Status);
        Assert.Equal("ACH-TRF-2026-99901", result.Value.PaymentReference);
        Assert.NotNull(result.Value.PaidAt);

        var updatedClaim = await _dbContext.Claims.Include(c => c.History).FirstAsync(c => c.Id == claim.Id);
        Assert.Equal(ClaimStatus.Paid, updatedClaim.Status);
        Assert.Equal("ACH-TRF-2026-99901", updatedClaim.PaymentReference);
        Assert.NotNull(updatedClaim.PaidAt);
        Assert.Contains(updatedClaim.History, h => h.NewStatus == ClaimStatus.Paid && h.ChangedById == 10);
    }

    #endregion
}
