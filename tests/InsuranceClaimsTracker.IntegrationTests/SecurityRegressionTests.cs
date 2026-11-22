using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InsuranceClaimsTracker.Application.DTOs.Admin;
using InsuranceClaimsTracker.Application.DTOs.Auth;
using InsuranceClaimsTracker.Application.DTOs.Claims;
using InsuranceClaimsTracker.Application.DTOs.Policies;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InsuranceClaimsTracker.IntegrationTests;

public class SecurityRegressionTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SecurityRegressionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // =========================================================================
    // 1. Authentication Security Tests
    // =========================================================================

    [Fact]
    public async Task Request_WithMissingJwt_ReturnsUnauthorized401()
    {
        var response = await _client.GetAsync("/api/v1/policies");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var meResponse = await _client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);

        var claimsResponse = await _client.GetAsync("/api/v1/claims");
        Assert.Equal(HttpStatusCode.Unauthorized, claimsResponse.StatusCode);
    }

    [Fact]
    public async Task Request_WithMalformedJwt_ReturnsUnauthorized401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/policies");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-valid-jwt-token.malformed.header");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithExpiredJwt_ReturnsUnauthorized401()
    {
        var expiredToken = TestTokenHelper.CreateToken(
            userId: 101,
            role: "Customer",
            expires: DateTime.UtcNow.AddMinutes(-10));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/policies");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithInvalidJwtSignature_ReturnsUnauthorized401()
    {
        var forgedToken = TestTokenHelper.CreateToken(
            userId: 101,
            role: "Admin",
            signingKey: "ForgedDifferentSecretKeyWithSufficientLength32Bytes!!");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/policies");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // 2. Authorization Security Tests
    // =========================================================================

    [Fact]
    public async Task Customer_CannotCreatePolicy_ReturnsForbidden403()
    {
        var customerToken = TestTokenHelper.CreateToken(userId: 201, role: "Customer");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/policies")
        {
            Content = JsonContent.Create(new CreatePolicyRequestDto
            {
                PolicyNumber = $"POL-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant(),
                CustomerId = 201,
                PolicyType = PolicyType.Auto,
                CoverageAmount = 25000m,
                StartDate = DateTimeOffset.UtcNow,
                EndDate = DateTimeOffset.UtcNow.AddYears(1)
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotStartReview_ReturnsForbidden403()
    {
        var customerToken = TestTokenHelper.CreateToken(userId: 202, role: "Customer");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/claims/1/start-review")
        {
            Content = JsonContent.Create(new StartReviewRequestDto
            {
                RowVersion = Convert.ToBase64String([1, 2, 3, 4])
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotApproveOrRejectClaims_ReturnsForbidden403()
    {
        var customerToken = TestTokenHelper.CreateToken(userId: 203, role: "Customer");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/claims/1/decision")
        {
            Content = JsonContent.Create(new ClaimDecisionRequestDto
            {
                Decision = "Approved",
                ApprovedAmount = 1000m,
                ReviewNote = "Self-approval attempt",
                RowVersion = Convert.ToBase64String([1, 2, 3, 4])
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotRecordPayouts_ReturnsForbidden403()
    {
        var customerToken = TestTokenHelper.CreateToken(userId: 204, role: "Customer");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/claims/1/payout")
        {
            Content = JsonContent.Create(new ClaimPayoutRequestDto
            {
                PaymentReference = "PAY-EXPLOIT-01",
                RowVersion = Convert.ToBase64String([1, 2, 3, 4])
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublicRegistration_CannotCreateAdminAccount_RoleParameterIsIgnored()
    {
        var email = $"exploit_admin_{Guid.NewGuid():N}@example.com";
        var payloadWithRole = new
        {
            name = "Attacker Trying Admin",
            email = email,
            password = "Password123456!",
            role = "Admin"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", payloadWithRole);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var registered = await response.Content.ReadFromJsonAsync<UserProfileDto>(JsonOptions);
        Assert.NotNull(registered);
        Assert.Equal("Customer", registered.Role);

        // Verify in database that account is strictly Customer
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userInDb = await db.Users.FindAsync(registered.Id);
        Assert.NotNull(userInDb);
        Assert.Equal(UserRole.Customer, userInDb.Role);
    }

    // =========================================================================
    // 3. BOLA / IDOR Defense Tests
    // =========================================================================

    [Fact]
    public async Task CustomerA_CannotRetrieveCustomerBsPolicy_ReturnsNotFound404()
    {
        // Arrange: Seed two distinct customers and a policy for Customer B
        int customerAId;
        int policyBId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var custA = new User("Customer A", $"custA_{Guid.NewGuid():N}@example.com", "hash", UserRole.Customer);
            var custB = new User("Customer B", $"custB_{Guid.NewGuid():N}@example.com", "hash", UserRole.Customer);
            db.Users.AddRange(custA, custB);
            await db.SaveChangesAsync();

            var policyB = new Policy(
                $"POL-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant(),
                custB.Id,
                PolicyType.Auto,
                20000m,
                DateTimeOffset.UtcNow.AddMonths(-1),
                DateTimeOffset.UtcNow.AddMonths(11));
            db.Policies.Add(policyB);
            await db.SaveChangesAsync();

            customerAId = custA.Id;
            policyBId = policyB.Id;
        }

        var customerAToken = TestTokenHelper.CreateToken(customerAId, role: "Customer");

        // Act: Customer A requests Customer B's policy
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/policies/{policyBId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerAToken);

        var response = await _client.SendAsync(request);

        // Assert: Returns 404 (prevents resource enumeration / disclosure of foreign resources)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Verify error body does not leak Customer B details
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Customer B", responseBody);
        Assert.DoesNotContain("custB_", responseBody);
    }

    [Fact]
    public async Task CustomerA_CannotRetrieveCustomerBsClaim_ReturnsNotFound404()
    {
        int customerAId;
        int claimBId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var custA = new User("Customer A2", $"custA2_{Guid.NewGuid():N}@example.com", "hash", UserRole.Customer);
            var custB = new User("Customer B2", $"custB2_{Guid.NewGuid():N}@example.com", "hash", UserRole.Customer);
            db.Users.AddRange(custA, custB);
            await db.SaveChangesAsync();

            var policyB = new Policy(
                $"POL-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant(),
                custB.Id,
                PolicyType.Home,
                40000m,
                DateTimeOffset.UtcNow.AddMonths(-1),
                DateTimeOffset.UtcNow.AddMonths(11));
            db.Policies.Add(policyB);
            await db.SaveChangesAsync();

            var claimB = new Claim(policyB.Id, 2500m, DateTimeOffset.UtcNow.AddDays(-2), "Broken window at Customer B house.");
            db.Claims.Add(claimB);
            await db.SaveChangesAsync();

            customerAId = custA.Id;
            claimBId = claimB.Id;
        }

        var customerAToken = TestTokenHelper.CreateToken(customerAId, role: "Customer");

        // Act: Customer A attempts to get Customer B's claim
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/claims/{claimBId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerAToken);

        var response = await _client.SendAsync(request);

        // Assert: 404
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Broken window", responseBody);
        Assert.DoesNotContain("Customer B2", responseBody);
    }

    [Fact]
    public async Task CustomerA_CannotSubmitClaimAgainstCustomerBsPolicy_ReturnsNotFound404()
    {
        int customerAId;
        int policyBId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var custA = new User("Customer A3", $"custA3_{Guid.NewGuid():N}@example.com", "hash", UserRole.Customer);
            var custB = new User("Customer B3", $"custB3_{Guid.NewGuid():N}@example.com", "hash", UserRole.Customer);
            db.Users.AddRange(custA, custB);
            await db.SaveChangesAsync();

            var policyB = new Policy(
                $"POL-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant(),
                custB.Id,
                PolicyType.Auto,
                15000m,
                DateTimeOffset.UtcNow.AddMonths(-1),
                DateTimeOffset.UtcNow.AddMonths(11));
            db.Policies.Add(policyB);
            await db.SaveChangesAsync();

            customerAId = custA.Id;
            policyBId = policyB.Id;
        }

        var customerAToken = TestTokenHelper.CreateToken(customerAId, role: "Customer");

        // Act: Customer A attempts to submit a claim using policyBId
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims")
        {
            Content = JsonContent.Create(new CreateClaimRequestDto
            {
                PolicyId = policyBId,
                ClaimAmount = 1500m,
                IncidentDate = DateTimeOffset.UtcNow.AddDays(-1),
                Description = "Fraudulent claim attempt against someone else's policy."
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerAToken);

        var response = await _client.SendAsync(request);

        // Assert: Returns 404 so caller cannot deduce whether policy exists
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Customer_GetPoliciesAndClaims_OnlyReturnsOwnResources()
    {
        int cust1Id, cust2Id;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cust1 = new User("Isolated Cust 1", $"iso1_{Guid.NewGuid():N}@example.com", "hash", UserRole.Customer);
            var cust2 = new User("Isolated Cust 2", $"iso2_{Guid.NewGuid():N}@example.com", "hash", UserRole.Customer);
            db.Users.AddRange(cust1, cust2);
            await db.SaveChangesAsync();

            var pol1 = new Policy($"POL-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant(), cust1.Id, PolicyType.Auto, 10000m, DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(100));
            var pol2 = new Policy($"POL-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant(), cust2.Id, PolicyType.Life, 100000m, DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(100));
            db.Policies.AddRange(pol1, pol2);
            await db.SaveChangesAsync();

            var clm1 = new Claim(pol1.Id, 1000m, DateTimeOffset.UtcNow.AddDays(-5), "Claim 1");
            var clm2 = new Claim(pol2.Id, 2000m, DateTimeOffset.UtcNow.AddDays(-5), "Claim 2");
            db.Claims.AddRange(clm1, clm2);
            await db.SaveChangesAsync();

            cust1Id = cust1.Id;
            cust2Id = cust2.Id;
        }

        var cust1Token = TestTokenHelper.CreateToken(cust1Id, role: "Customer");

        // Policies query for Cust 1
        using var polReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/policies");
        polReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cust1Token);
        var polRes = await _client.SendAsync(polReq);
        var policies = await polRes.Content.ReadFromJsonAsync<List<PolicyResponseDto>>(JsonOptions);

        Assert.NotNull(policies);
        Assert.All(policies, p => Assert.Equal(cust1Id, p.CustomerId));

        // Claims query for Cust 1
        using var clmReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/claims");
        clmReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cust1Token);
        var clmRes = await _client.SendAsync(clmReq);
        var claims = await clmRes.Content.ReadFromJsonAsync<List<ClaimResponseDto>>(JsonOptions);

        Assert.NotNull(claims);
        Assert.All(claims, c => Assert.Equal(cust1Id, c.CustomerId));
    }
}
