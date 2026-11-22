using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InsuranceClaimsTracker.Application.DTOs.Claims;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InsuranceClaimsTracker.IntegrationTests;

public class ClaimEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ClaimEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(User customer, Policy policy)> CreateCustomerAndPolicyAsync(
        decimal coverage = 20000m,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var customer = new User("Claim Customer", $"claim_cust_{Guid.NewGuid():N}@example.com", "hash", UserRole.Customer);
        db.Users.Add(customer);
        await db.SaveChangesAsync();

        var policy = new Policy(
            $"POL-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant(),
            customer.Id,
            PolicyType.Auto,
            coverage,
            startDate ?? DateTimeOffset.UtcNow.AddMonths(-6),
            endDate ?? DateTimeOffset.UtcNow.AddMonths(6))
        {
            IsActive = isActive
        };

        db.Policies.Add(policy);
        await db.SaveChangesAsync();

        return (customer, policy);
    }

    [Fact]
    public async Task Customer_CanSubmitClaim_ReturnsCreatedAndInitialHistory()
    {
        // Arrange
        var (customer, policy) = await CreateCustomerAndPolicyAsync(coverage: 25000m);
        var customerToken = TestTokenHelper.CreateToken(customer.Id, role: "Customer", email: customer.Email, name: customer.Name);

        var request = new CreateClaimRequestDto
        {
            PolicyId = policy.Id,
            ClaimAmount = 4500.00m,
            IncidentDate = DateTimeOffset.UtcNow.AddDays(-3),
            Description = "Collision with a deer on country road."
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        // Act
        var response = await _client.SendAsync(httpRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize<ClaimResponseDto>(content, JsonOptions);

        Assert.NotNull(created);
        Assert.Equal("Submitted", created.Status);
        Assert.Equal(4500.00m, created.ClaimAmount);
        Assert.Equal(policy.Id, created.PolicyId);
        Assert.Equal(customer.Id, created.CustomerId);

        // Verify initial ClaimHistory
        Assert.Single(created.History);
        var history = created.History[0];
        Assert.Null(history.PreviousStatus);
        Assert.Equal("Submitted", history.NewStatus);
        Assert.Equal(customer.Id, history.ChangedById);
    }

    [Fact]
    public async Task Admin_CannotSubmitClaim_ReturnsForbidden403()
    {
        // Arrange
        var (customer, policy) = await CreateCustomerAndPolicyAsync();
        var adminToken = TestTokenHelper.CreateToken(99, role: "Admin");

        var request = new CreateClaimRequestDto
        {
            PolicyId = policy.Id,
            ClaimAmount = 500m,
            IncidentDate = DateTimeOffset.UtcNow.AddDays(-1),
            Description = "Admin attempting to submit claim."
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Act
        var response = await _client.SendAsync(httpRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_CannotSubmitClaim_ReturnsUnauthorized401()
    {
        // Arrange
        var request = new CreateClaimRequestDto
        {
            PolicyId = 1,
            ClaimAmount = 1000m,
            IncidentDate = DateTimeOffset.UtcNow.AddDays(-1),
            Description = "No auth claim."
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/claims", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotSubmitClaimAgainstOtherCustomerPolicy_ReturnsNotFound404()
    {
        // Arrange
        var (customer1, _) = await CreateCustomerAndPolicyAsync();
        var (_, policy2) = await CreateCustomerAndPolicyAsync();

        // Customer 1 tries to submit claim against Customer 2's policy
        var customer1Token = TestTokenHelper.CreateToken(customer1.Id, role: "Customer");

        var request = new CreateClaimRequestDto
        {
            PolicyId = policy2.Id,
            ClaimAmount = 1000m,
            IncidentDate = DateTimeOffset.UtcNow.AddDays(-1),
            Description = "Claim against other customer policy."
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customer1Token);

        // Act
        var response = await _client.SendAsync(httpRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Customer_SubmitClaim_WithAmountExceedingCoverage_ReturnsBadRequest400()
    {
        // Arrange
        var (customer, policy) = await CreateCustomerAndPolicyAsync(coverage: 5000m);
        var customerToken = TestTokenHelper.CreateToken(customer.Id, role: "Customer");

        var request = new CreateClaimRequestDto
        {
            PolicyId = policy.Id,
            ClaimAmount = 5000.01m, // Exceeds coverage
            IncidentDate = DateTimeOffset.UtcNow.AddDays(-1),
            Description = "Amount exceeds coverage limit."
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        // Act
        var response = await _client.SendAsync(httpRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Customer_SubmitClaim_WithIncidentDateOutsideCoverage_ReturnsBadRequest400()
    {
        // Arrange: policy covers from 3 months ago to 3 months from now
        var (customer, policy) = await CreateCustomerAndPolicyAsync(
            startDate: DateTimeOffset.UtcNow.AddMonths(-3),
            endDate: DateTimeOffset.UtcNow.AddMonths(3));
        var customerToken = TestTokenHelper.CreateToken(customer.Id, role: "Customer");

        var request = new CreateClaimRequestDto
        {
            PolicyId = policy.Id,
            ClaimAmount = 500m,
            IncidentDate = DateTimeOffset.UtcNow.AddMonths(-5), // Prior to policy start
            Description = "Incident prior to policy inception."
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        // Act
        var response = await _client.SendAsync(httpRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Customer_SubmitClaim_WithIncidentDateInFuture_ReturnsBadRequest400()
    {
        // Arrange
        var (customer, policy) = await CreateCustomerAndPolicyAsync();
        var customerToken = TestTokenHelper.CreateToken(customer.Id, role: "Customer");

        var request = new CreateClaimRequestDto
        {
            PolicyId = policy.Id,
            ClaimAmount = 500m,
            IncidentDate = DateTimeOffset.UtcNow.AddDays(2), // Future
            Description = "Future date claim."
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        // Act
        var response = await _client.SendAsync(httpRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Customer_GetClaims_ReturnsOnlyOwnClaims()
    {
        // Arrange
        var (customer1, policy1) = await CreateCustomerAndPolicyAsync();
        var (customer2, policy2) = await CreateCustomerAndPolicyAsync();

        // Customer 1 submits claim
        var token1 = TestTokenHelper.CreateToken(customer1.Id, role: "Customer");
        using var postReq1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims")
        {
            Content = JsonContent.Create(new CreateClaimRequestDto
            {
                PolicyId = policy1.Id,
                ClaimAmount = 1000m,
                IncidentDate = DateTimeOffset.UtcNow.AddDays(-2),
                Description = "Customer 1 claim."
            })
        };
        postReq1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var postRes1 = await _client.SendAsync(postReq1);
        var claim1 = JsonSerializer.Deserialize<ClaimResponseDto>(await postRes1.Content.ReadAsStringAsync(), JsonOptions);

        // Customer 2 submits claim
        var token2 = TestTokenHelper.CreateToken(customer2.Id, role: "Customer");
        using var postReq2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims")
        {
            Content = JsonContent.Create(new CreateClaimRequestDto
            {
                PolicyId = policy2.Id,
                ClaimAmount = 2000m,
                IncidentDate = DateTimeOffset.UtcNow.AddDays(-2),
                Description = "Customer 2 claim."
            })
        };
        postReq2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var postRes2 = await _client.SendAsync(postReq2);
        var claim2 = JsonSerializer.Deserialize<ClaimResponseDto>(await postRes2.Content.ReadAsStringAsync(), JsonOptions);

        // Customer 1 requests claims
        using var getReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/claims");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token1);

        // Act
        var getRes = await _client.SendAsync(getReq);

        // Assert
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var content = await getRes.Content.ReadAsStringAsync();
        var claims = JsonSerializer.Deserialize<List<ClaimResponseDto>>(content, JsonOptions);

        Assert.NotNull(claims);
        Assert.Contains(claims, c => c.Id == claim1!.Id);
        Assert.DoesNotContain(claims, c => c.Id == claim2!.Id);
    }

    [Fact]
    public async Task Customer_GetClaimById_ForOtherCustomerClaim_ReturnsNotFound404()
    {
        // Arrange
        var (customer1, _) = await CreateCustomerAndPolicyAsync();
        var (customer2, policy2) = await CreateCustomerAndPolicyAsync();

        // Customer 2 submits claim
        var token2 = TestTokenHelper.CreateToken(customer2.Id, role: "Customer");
        using var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims")
        {
            Content = JsonContent.Create(new CreateClaimRequestDto
            {
                PolicyId = policy2.Id,
                ClaimAmount = 1500m,
                IncidentDate = DateTimeOffset.UtcNow.AddDays(-2),
                Description = "Customer 2 claim."
            })
        };
        postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var postRes = await _client.SendAsync(postReq);
        var claim2 = JsonSerializer.Deserialize<ClaimResponseDto>(await postRes.Content.ReadAsStringAsync(), JsonOptions);

        // Customer 1 tries to GET Customer 2's claim by id
        var token1 = TestTokenHelper.CreateToken(customer1.Id, role: "Customer");
        using var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/claims/{claim2!.Id}");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token1);

        // Act
        var getRes = await _client.SendAsync(getReq);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);
    }
}
