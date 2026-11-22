using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InsuranceClaimsTracker.Application.DTOs.Admin;
using InsuranceClaimsTracker.Application.DTOs.Claims;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InsuranceClaimsTracker.IntegrationTests;

[Collection("SqlServerConcurrencyCollection")]
public class SqlServerConcurrencyRegressionTests : IClassFixture<SqlServerWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly SqlServerWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SqlServerConcurrencyRegressionTests(SqlServerWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(User admin, User customer, Policy policy, Claim claim)> SeedScenarioAsync(
        ClaimStatus initialStatus = ClaimStatus.Submitted,
        decimal claimAmount = 5000m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var admin = new User("Admin Conc", $"admin_conc_{Guid.NewGuid():N}@example.com", "hash", UserRole.Admin);
        var customer = new User("Customer Conc", $"cust_conc_{Guid.NewGuid():N}@example.com", "hash", UserRole.Customer);
        db.Users.AddRange(admin, customer);
        await db.SaveChangesAsync();

        var policy = new Policy(
            $"POL-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant(),
            customer.Id,
            PolicyType.Auto,
            30000m,
            DateTimeOffset.UtcNow.AddMonths(-2),
            DateTimeOffset.UtcNow.AddMonths(10));
        db.Policies.Add(policy);
        await db.SaveChangesAsync();

        var claim = new Claim(policy.Id, claimAmount, DateTimeOffset.UtcNow.AddDays(-5), "Rear bumper collision.")
        {
            Status = initialStatus
        };

        if (initialStatus == ClaimStatus.Approved)
        {
            claim.ApprovedAmount = claimAmount * 0.9m;
        }

        db.Claims.Add(claim);
        await db.SaveChangesAsync();

        // Reload claim to ensure native SQL Server rowversion is populated
        await db.Entry(claim).ReloadAsync();

        return (admin, customer, policy, claim);
    }

    [Fact]
    public async Task Concurrency_TwoIndependentRequests_SecondUpdateWithStaleRowVersion_ReturnsConflict409()
    {
        // -------------------------------------------------------------------------
        // 1. Seed claim in native SQL Server database
        // -------------------------------------------------------------------------
        var (admin, _, _, initialClaim) = await SeedScenarioAsync(ClaimStatus.Submitted, 6000m);
        var adminToken = TestTokenHelper.CreateToken(admin.Id, role: "Admin", email: admin.Email);

        // -------------------------------------------------------------------------
        // 2. Retrieve the same claim and RowVersion from two independent requests
        // -------------------------------------------------------------------------
        using var getReq1 = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/claims/{initialClaim.Id}");
        getReq1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var res1 = await _client.SendAsync(getReq1);
        var claimSnapshot1 = await res1.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(claimSnapshot1);
        Assert.NotNull(claimSnapshot1.RowVersion);

        using var getReq2 = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/claims/{initialClaim.Id}");
        getReq2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var res2 = await _client.SendAsync(getReq2);
        var claimSnapshot2 = await res2.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(claimSnapshot2);
        Assert.NotNull(claimSnapshot2.RowVersion);

        // Both snapshots initially hold the exact same RowVersion token
        Assert.Equal(claimSnapshot1.RowVersion, claimSnapshot2.RowVersion);

        // -------------------------------------------------------------------------
        // 3. Perform a successful update with Context 1 (Start Review)
        // -------------------------------------------------------------------------
        using var updateReq1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{initialClaim.Id}/start-review")
        {
            Content = JsonContent.Create(new StartReviewRequestDto
            {
                RowVersion = claimSnapshot1.RowVersion,
                ReviewNote = "Context 1 starting review."
            })
        };
        updateReq1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var updateRes1 = await _client.SendAsync(updateReq1);
        Assert.Equal(HttpStatusCode.OK, updateRes1.StatusCode);
        var updatedClaim1 = await updateRes1.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(updatedClaim1);
        Assert.Equal("UnderReview", updatedClaim1.Status);
        // SQL Server native rowversion has changed
        Assert.NotEqual(claimSnapshot1.RowVersion, updatedClaim1.RowVersion);

        // -------------------------------------------------------------------------
        // 4. Attempt an update using the stale RowVersion from Context 2
        // -------------------------------------------------------------------------
        using var staleUpdateReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{initialClaim.Id}/start-review")
        {
            Content = JsonContent.Create(new StartReviewRequestDto
            {
                RowVersion = claimSnapshot2.RowVersion, // Stale!
                ReviewNote = "Context 2 conflicting attempt."
            })
        };
        staleUpdateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var staleResponse = await _client.SendAsync(staleUpdateReq);

        // -------------------------------------------------------------------------
        // 5. Verify that the second update returns HTTP 409 Conflict
        // -------------------------------------------------------------------------
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        // -------------------------------------------------------------------------
        // 6. Confirm the first update remains unchanged in the database
        // -------------------------------------------------------------------------
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var claimInDb = await db.Claims
                .Include(c => c.History)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == initialClaim.Id);

            Assert.NotNull(claimInDb);
            Assert.Equal(ClaimStatus.UnderReview, claimInDb.Status);

            // -------------------------------------------------------------------------
            // 7. Confirm no extra or partially persisted ClaimHistory record was created
            // -------------------------------------------------------------------------
            // There should be only 1 history record for the first successful review transition
            Assert.Single(claimInDb.History);
            Assert.Equal(ClaimStatus.UnderReview, claimInDb.History.First().NewStatus);
            Assert.Equal("Context 1 starting review.", claimInDb.History.First().ReviewNote);
        }
    }

    [Fact]
    public async Task Concurrency_ConflictingDecisions_SecondDecisionWithStaleRowVersion_ReturnsConflict409()
    {
        // Arrange: Seed claim in UnderReview status
        var (admin, _, _, claim) = await SeedScenarioAsync(ClaimStatus.UnderReview, 4000m);
        var adminToken = TestTokenHelper.CreateToken(admin.Id, role: "Admin", email: admin.Email);

        // Admin 1 and Admin 2 both fetch the UnderReview claim
        using var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/claims/{claim.Id}");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var getRes = await _client.SendAsync(getReq);
        var currentClaim = await getRes.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        var staleRowVersion = currentClaim!.RowVersion!;

        // Admin 1 successfully approves the claim
        using var approveReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/decision")
        {
            Content = JsonContent.Create(new ClaimDecisionRequestDto
            {
                Decision = "Approved",
                ApprovedAmount = 3500m,
                ReviewNote = "Approved by first adjuster.",
                RowVersion = staleRowVersion
            })
        };
        approveReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var approveRes = await _client.SendAsync(approveReq);
        Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);

        // Admin 2 attempts to reject the claim using the stale token
        using var rejectReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/decision")
        {
            Content = JsonContent.Create(new ClaimDecisionRequestDto
            {
                Decision = "Rejected",
                ReviewNote = "Rejected by second adjuster with stale state.",
                RowVersion = staleRowVersion
            })
        };
        rejectReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var rejectRes = await _client.SendAsync(rejectReq);

        // Assert: 409 Conflict
        Assert.Equal(HttpStatusCode.Conflict, rejectRes.StatusCode);

        // Verify claim in DB remains Approved with 3500m
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var claimInDb = await db.Claims.FindAsync(claim.Id);
            Assert.NotNull(claimInDb);
            Assert.Equal(ClaimStatus.Approved, claimInDb.Status);
            Assert.Equal(3500m, claimInDb.ApprovedAmount);
        }
    }

    [Fact]
    public async Task Invariant_RejectedClaim_CannotBePaid_ReturnsBadRequest400()
    {
        // Arrange: Seed claim in Rejected status
        var (admin, _, _, claim) = await SeedScenarioAsync(ClaimStatus.Rejected, 3000m);
        var adminToken = TestTokenHelper.CreateToken(admin.Id, role: "Admin", email: admin.Email);

        // Act: Attempt payout on Rejected claim
        using var payoutReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/payout")
        {
            Content = JsonContent.Create(new ClaimPayoutRequestDto
            {
                PaymentReference = "PAY-REJECTED-FAIL",
                RowVersion = Convert.ToBase64String(claim.RowVersion!)
            })
        };
        payoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var payoutRes = await _client.SendAsync(payoutReq);

        // Assert: 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, payoutRes.StatusCode);

        // Verify claim remains Rejected in database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var claimInDb = await db.Claims.FindAsync(claim.Id);
            Assert.NotNull(claimInDb);
            Assert.Equal(ClaimStatus.Rejected, claimInDb.Status);
            Assert.Null(claimInDb.PaymentReference);
            Assert.Null(claimInDb.PaidAt);
        }
    }

    [Fact]
    public async Task Invariant_ApprovedClaim_CannotReceiveDuplicatePayouts()
    {
        // Arrange: Seed claim in Approved status
        var (admin, _, _, claim) = await SeedScenarioAsync(ClaimStatus.Approved, 2000m);
        var adminToken = TestTokenHelper.CreateToken(admin.Id, role: "Admin", email: admin.Email);

        // First payout succeeds
        using var payoutReq1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/payout")
        {
            Content = JsonContent.Create(new ClaimPayoutRequestDto
            {
                PaymentReference = "WIRE-FIRST-SUCCESS",
                RowVersion = Convert.ToBase64String(claim.RowVersion!)
            })
        };
        payoutReq1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var payoutRes1 = await _client.SendAsync(payoutReq1);
        Assert.Equal(HttpStatusCode.OK, payoutRes1.StatusCode);
        var paidClaim = await payoutRes1.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(paidClaim);
        Assert.Equal("Paid", paidClaim.Status);

        // Second payout attempt (using updated rowversion) must fail because claim is already Paid
        using var payoutReq2 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/payout")
        {
            Content = JsonContent.Create(new ClaimPayoutRequestDto
            {
                PaymentReference = "WIRE-SECOND-DUPLICATE",
                RowVersion = paidClaim.RowVersion!
            })
        };
        payoutReq2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var payoutRes2 = await _client.SendAsync(payoutReq2);

        // Assert: 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, payoutRes2.StatusCode);

        // Verify database: Payment reference remains original, no duplicate payment
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var claimInDb = await db.Claims.FindAsync(claim.Id);
            Assert.NotNull(claimInDb);
            Assert.Equal(ClaimStatus.Paid, claimInDb.Status);
            Assert.Equal("WIRE-FIRST-SUCCESS", claimInDb.PaymentReference);
        }
    }
}
