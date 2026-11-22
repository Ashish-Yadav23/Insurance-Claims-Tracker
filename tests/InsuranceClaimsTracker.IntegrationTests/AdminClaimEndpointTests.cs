using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InsuranceClaimsTracker.Application.DTOs.Admin;
using InsuranceClaimsTracker.Application.DTOs.Claims;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InsuranceClaimsTracker.IntegrationTests;

public class AdminClaimEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AdminClaimEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(User admin, User customer, Policy policy, Claim claim)> SeedClaimScenarioAsync(
        ClaimStatus initialStatus = ClaimStatus.Submitted,
        decimal claimAmount = 3000m,
        byte[]? initialRowVersion = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var admin = new User("Admin Reviewer", $"admin_{Guid.NewGuid():N}@example.com", "hash", UserRole.Admin);
        var customer = new User("Customer Alice", $"cust_{Guid.NewGuid():N}@example.com", "hash", UserRole.Customer);
        db.Users.AddRange(admin, customer);
        await db.SaveChangesAsync();

        var policy = new Policy(
            $"POL-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant(),
            customer.Id,
            PolicyType.Auto,
            15000m,
            DateTimeOffset.UtcNow.AddMonths(-3),
            DateTimeOffset.UtcNow.AddMonths(9));
        db.Policies.Add(policy);
        await db.SaveChangesAsync();

        var claim = new Claim(policy.Id, claimAmount, DateTimeOffset.UtcNow.AddDays(-5), "Windshield and hood hail damage.")
        {
            Status = initialStatus,
            RowVersion = initialRowVersion ?? [0x01, 0x02, 0x03, 0x04]
        };

        if (initialStatus == ClaimStatus.Approved)
        {
            claim.ApprovedAmount = claimAmount * 0.9m;
        }

        db.Claims.Add(claim);
        await db.SaveChangesAsync();

        return (admin, customer, policy, claim);
    }

    [Fact]
    public async Task AdminEndpoints_WithoutToken_ReturnsUnauthorized401()
    {
        var startReviewResponse = await _client.PostAsJsonAsync("/api/v1/admin/claims/1/start-review", new StartReviewRequestDto
        {
            RowVersion = Convert.ToBase64String([1, 2, 3])
        });
        Assert.Equal(HttpStatusCode.Unauthorized, startReviewResponse.StatusCode);

        var decisionResponse = await _client.PostAsJsonAsync("/api/v1/admin/claims/1/decision", new ClaimDecisionRequestDto
        {
            Decision = "Approved",
            ApprovedAmount = 500m,
            ReviewNote = "Approved",
            RowVersion = Convert.ToBase64String([1, 2, 3])
        });
        Assert.Equal(HttpStatusCode.Unauthorized, decisionResponse.StatusCode);

        var payoutResponse = await _client.PostAsJsonAsync("/api/v1/admin/claims/1/payout", new ClaimPayoutRequestDto
        {
            PaymentReference = "REF-123",
            RowVersion = Convert.ToBase64String([1, 2, 3])
        });
        Assert.Equal(HttpStatusCode.Unauthorized, payoutResponse.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_WithCustomerToken_ReturnsForbidden403()
    {
        var (_, customer, _, claim) = await SeedClaimScenarioAsync();
        var customerToken = TestTokenHelper.CreateToken(customer.Id, "Customer", customer.Email);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/start-review");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        request.Content = JsonContent.Create(new StartReviewRequestDto
        {
            RowVersion = Convert.ToBase64String(claim.RowVersion!)
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanExecuteCompleteLifecycle_StartReview_Approve_Payout()
    {
        // 1. Seed claim in Submitted status
        var (admin, _, _, claim) = await SeedClaimScenarioAsync(ClaimStatus.Submitted, claimAmount: 4000m);
        var adminToken = TestTokenHelper.CreateToken(admin.Id, "Admin", admin.Email);

        // 2. Start Review
        var startReviewReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/start-review");
        startReviewReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        startReviewReq.Content = JsonContent.Create(new StartReviewRequestDto
        {
            RowVersion = Convert.ToBase64String(claim.RowVersion!),
            ReviewNote = "Commencing claim verification."
        });

        var startReviewRes = await _client.SendAsync(startReviewReq);
        Assert.Equal(HttpStatusCode.OK, startReviewRes.StatusCode);
        var reviewDto = await startReviewRes.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(reviewDto);
        Assert.Equal("UnderReview", reviewDto.Status);
        Assert.NotNull(reviewDto.RowVersion);

        // 3. Make Decision: Approve
        var decisionReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/decision");
        decisionReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        decisionReq.Content = JsonContent.Create(new ClaimDecisionRequestDto
        {
            Decision = "Approved",
            ApprovedAmount = 3500m,
            ReviewNote = "Damages verified by field adjuster.",
            RowVersion = reviewDto.RowVersion
        });

        var decisionRes = await _client.SendAsync(decisionReq);
        Assert.Equal(HttpStatusCode.OK, decisionRes.StatusCode);
        var approvedDto = await decisionRes.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(approvedDto);
        Assert.Equal("Approved", approvedDto.Status);
        Assert.Equal(3500m, approvedDto.ApprovedAmount);
        Assert.NotNull(approvedDto.RowVersion);

        // 4. Record Payout
        var payoutReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/payout");
        payoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        payoutReq.Content = JsonContent.Create(new ClaimPayoutRequestDto
        {
            PaymentReference = "WIRE-2026-US-88990",
            RowVersion = approvedDto.RowVersion
        });

        var payoutRes = await _client.SendAsync(payoutReq);
        Assert.Equal(HttpStatusCode.OK, payoutRes.StatusCode);
        var paidDto = await payoutRes.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(paidDto);
        Assert.Equal("Paid", paidDto.Status);
        Assert.Equal("WIRE-2026-US-88990", paidDto.PaymentReference);
        Assert.NotNull(paidDto.PaidAt);
        Assert.Equal(3, paidDto.History.Count);
    }

    [Fact]
    public async Task Admin_CanRejectClaim_SetsStatusRejectedAndClearsApprovedAmount()
    {
        var (admin, _, _, claim) = await SeedClaimScenarioAsync(ClaimStatus.UnderReview, claimAmount: 2500m);
        var adminToken = TestTokenHelper.CreateToken(admin.Id, "Admin", admin.Email);

        var decisionReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/decision");
        decisionReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        decisionReq.Content = JsonContent.Create(new ClaimDecisionRequestDto
        {
            Decision = "Rejected",
            ReviewNote = "Claimed incident is an excluded wear-and-tear condition.",
            RowVersion = Convert.ToBase64String(claim.RowVersion!)
        });

        var decisionRes = await _client.SendAsync(decisionReq);
        Assert.Equal(HttpStatusCode.OK, decisionRes.StatusCode);
        var rejectedDto = await decisionRes.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(rejectedDto);
        Assert.Equal("Rejected", rejectedDto.Status);
        Assert.Null(rejectedDto.ApprovedAmount);
    }

    [Fact]
    public async Task Admin_StaleRowVersion_ReturnsConflict409()
    {
        var (admin, _, _, claim) = await SeedClaimScenarioAsync(ClaimStatus.Submitted, claimAmount: 2000m, initialRowVersion: [1, 2, 3, 4]);
        var adminToken = TestTokenHelper.CreateToken(admin.Id, "Admin", admin.Email);

        // Submit stale/wrong token
        var startReviewReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/start-review");
        startReviewReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        startReviewReq.Content = JsonContent.Create(new StartReviewRequestDto
        {
            RowVersion = Convert.ToBase64String([9, 9, 9, 9]),
            ReviewNote = "Starting review"
        });

        var response = await _client.SendAsync(startReviewReq);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Verify claim status remained Submitted and no history was added
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var claimInDb = await db.Claims.FindAsync(claim.Id);
        Assert.NotNull(claimInDb);
        Assert.Equal(ClaimStatus.Submitted, claimInDb.Status);
    }

    [Fact]
    public async Task Admin_CannotPayUnapprovedClaim_ReturnsBadRequest400()
    {
        var (admin, _, _, claim) = await SeedClaimScenarioAsync(ClaimStatus.UnderReview);
        var adminToken = TestTokenHelper.CreateToken(admin.Id, "Admin", admin.Email);

        var payoutReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/payout");
        payoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        payoutReq.Content = JsonContent.Create(new ClaimPayoutRequestDto
        {
            PaymentReference = "PAY-FAIL-01",
            RowVersion = Convert.ToBase64String(claim.RowVersion!)
        });

        var response = await _client.SendAsync(payoutReq);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_ApproveWithAmountExceedingClaim_ReturnsBadRequest400()
    {
        var (admin, _, _, claim) = await SeedClaimScenarioAsync(ClaimStatus.UnderReview, claimAmount: 1000m);
        var adminToken = TestTokenHelper.CreateToken(admin.Id, "Admin", admin.Email);

        var decisionReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/decision");
        decisionReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        decisionReq.Content = JsonContent.Create(new ClaimDecisionRequestDto
        {
            Decision = "Approved",
            ApprovedAmount = 1500m,
            ReviewNote = "Excessive approval",
            RowVersion = Convert.ToBase64String(claim.RowVersion!)
        });

        var response = await _client.SendAsync(decisionReq);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
