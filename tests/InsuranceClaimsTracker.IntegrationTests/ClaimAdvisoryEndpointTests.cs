using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Application.DTOs.Admin;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InsuranceClaimsTracker.IntegrationTests;

public class ClaimAdvisoryEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ClaimAdvisoryEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(User admin, User customer, Policy policy, Claim claim)> SeedClaimScenarioAsync(
        ClaimStatus initialStatus = ClaimStatus.Submitted,
        decimal claimAmount = 5000m,
        string description = "Heavy rain caused water intrusion and drywall damage.")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var admin = new User("Admin Reviewer", $"admin_adv_{Guid.NewGuid():N}@example.com", "hash", UserRole.Admin);
        var customer = new User("Customer Alice", $"cust_adv_{Guid.NewGuid():N}@example.com", "hash", UserRole.Customer);
        db.Users.AddRange(admin, customer);
        await db.SaveChangesAsync();

        var policy = new Policy(
            $"POL-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant(),
            customer.Id,
            PolicyType.Home,
            40000m,
            DateTimeOffset.UtcNow.AddMonths(-3),
            DateTimeOffset.UtcNow.AddMonths(9));
        db.Policies.Add(policy);
        await db.SaveChangesAsync();

        var claim = new Claim(policy.Id, claimAmount, DateTimeOffset.UtcNow.AddDays(-5), description)
        {
            Status = initialStatus,
            RowVersion = [0x01, 0x02, 0x03, 0x04]
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
    public async Task Admin_CanAccessAdvisoryEndpoint_ReturnsStructuredAdvisoryResponse()
    {
        // Arrange
        var (admin, _, _, claim) = await SeedClaimScenarioAsync();
        var adminToken = TestTokenHelper.CreateToken(admin.Id, role: "Admin", email: admin.Email);

        _factory.MockLlmClient.ResultToReturn = LlmAdvisoryResult.Success(
            "{\"category\": \"WaterDamage\", \"summary\": \"Rainwater intrusion resulting in drywall deterioration.\", \"suggestedPriority\": \"Medium\"}");

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/advisory");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var advisory = await response.Content.ReadFromJsonAsync<ClaimAdvisoryResponseDto>(JsonOptions);
        Assert.NotNull(advisory);
        Assert.Equal("WaterDamage", advisory.Category);
        Assert.Equal("Rainwater intrusion resulting in drywall deterioration.", advisory.Summary);
        Assert.Equal("Medium", advisory.SuggestedPriority);

        // Verify that the prompt sent to LLM used server-retrieved data without PII
        Assert.NotNull(_factory.MockLlmClient.LastRequest);
        Assert.Equal("Home", _factory.MockLlmClient.LastRequest.PolicyType);
        Assert.Equal(5000m, _factory.MockLlmClient.LastRequest.ClaimAmount);
        Assert.Equal(claim.Description, _factory.MockLlmClient.LastRequest.Description);
    }

    [Fact]
    public async Task Customer_CannotAccessAdvisoryEndpoint_ReturnsForbidden403()
    {
        var (_, customer, _, claim) = await SeedClaimScenarioAsync();
        var customerToken = TestTokenHelper.CreateToken(customer.Id, role: "Customer", email: customer.Email);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/advisory");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_CannotAccessAdvisoryEndpoint_ReturnsUnauthorized401()
    {
        var (_, _, _, claim) = await SeedClaimScenarioAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/advisory");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_MissingClaim_ReturnsNotFound404()
    {
        var (admin, _, _, _) = await SeedClaimScenarioAsync();
        var adminToken = TestTokenHelper.CreateToken(admin.Id, role: "Admin", email: admin.Email);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/claims/999999/advisory");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_WhenLlmProviderFails_ReturnsServiceUnavailable503WithoutModifyingClaim()
    {
        var (admin, _, _, claim) = await SeedClaimScenarioAsync(ClaimStatus.UnderReview, 3000m);
        var adminToken = TestTokenHelper.CreateToken(admin.Id, role: "Admin", email: admin.Email);

        _factory.MockLlmClient.ResultToReturn = LlmAdvisoryResult.Failure(
            "The AI advisory service is temporarily unavailable. Please try again later.", 503);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/advisory");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        // Verify claim state, payout, and history in DB are completely unmodified
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var claimInDb = await db.Claims.Include(c => c.History).FirstAsync(c => c.Id == claim.Id);

        Assert.Equal(ClaimStatus.UnderReview, claimInDb.Status);
        Assert.Equal(3000m, claimInDb.ClaimAmount);
        Assert.Null(claimInDb.ApprovedAmount);
        Assert.Null(claimInDb.PaymentReference);
        Assert.Null(claimInDb.PaidAt);
        Assert.Empty(claimInDb.History);
    }

    [Fact]
    public async Task Admin_SuccessfulAdvisory_PreservesClaimStatusAndHistory()
    {
        var (admin, _, _, claim) = await SeedClaimScenarioAsync(ClaimStatus.UnderReview, 7500m);
        var adminToken = TestTokenHelper.CreateToken(admin.Id, role: "Admin", email: admin.Email);

        _factory.MockLlmClient.ResultToReturn = LlmAdvisoryResult.Success(
            "{\"category\": \"PropertyDamage\", \"summary\": \"Wind damage to structure.\", \"suggestedPriority\": \"High\"}");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claim.Id}/advisory");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify claim status remains UnderReview, no audit history was injected
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var claimInDb = await db.Claims.Include(c => c.History).FirstAsync(c => c.Id == claim.Id);

        Assert.Equal(ClaimStatus.UnderReview, claimInDb.Status);
        Assert.Empty(claimInDb.History);
    }
}
