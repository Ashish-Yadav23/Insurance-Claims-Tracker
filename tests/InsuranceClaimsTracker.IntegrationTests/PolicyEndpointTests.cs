using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InsuranceClaimsTracker.Application.DTOs.Policies;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InsuranceClaimsTracker.IntegrationTests;

public class PolicyEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PolicyEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<User> CreateUserAsync(string name, string email, UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new User(name, email, "hashedPassword123", role);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Admin_CanCreatePolicy_ReturnsCreatedAndValidDto()
    {
        // Arrange
        var customer = await CreateUserAsync("Policy Customer 1", $"cust_{Guid.NewGuid():N}@example.com", UserRole.Customer);
        var admin = await CreateUserAsync("Admin User", $"admin_{Guid.NewGuid():N}@example.com", UserRole.Admin);

        var adminToken = TestTokenHelper.CreateToken(admin.Id, role: "Admin", email: admin.Email, name: admin.Name);
        var policyNumber = $"POL-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant();

        var request = new CreatePolicyRequestDto
        {
            PolicyNumber = policyNumber,
            CustomerId = customer.Id,
            PolicyType = PolicyType.Auto,
            CoverageAmount = 25000m,
            StartDate = DateTimeOffset.UtcNow.AddDays(-10),
            EndDate = DateTimeOffset.UtcNow.AddYears(1)
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/policies")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Act
        var response = await _client.SendAsync(httpRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize<PolicyResponseDto>(content, JsonOptions);

        Assert.NotNull(created);
        Assert.Equal(policyNumber, created.PolicyNumber);
        Assert.Equal(customer.Id, created.CustomerId);
        Assert.Equal(25000m, created.CoverageAmount);
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task Customer_CannotCreatePolicy_ReturnsForbidden403()
    {
        // Arrange
        var customer = await CreateUserAsync("Customer Forbid", $"cust_{Guid.NewGuid():N}@example.com", UserRole.Customer);
        var customerToken = TestTokenHelper.CreateToken(customer.Id, role: "Customer");

        var request = new CreatePolicyRequestDto
        {
            PolicyNumber = "POL-FORBIDDEN",
            CustomerId = customer.Id,
            PolicyType = PolicyType.Home,
            CoverageAmount = 10000m,
            StartDate = DateTimeOffset.UtcNow,
            EndDate = DateTimeOffset.UtcNow.AddYears(1)
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/policies")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        // Act
        var response = await _client.SendAsync(httpRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_CannotCreatePolicy_ReturnsUnauthorized401()
    {
        // Arrange
        var request = new CreatePolicyRequestDto
        {
            PolicyNumber = "POL-NOAUTH",
            CustomerId = 1,
            PolicyType = PolicyType.Life,
            CoverageAmount = 50000m,
            StartDate = DateTimeOffset.UtcNow,
            EndDate = DateTimeOffset.UtcNow.AddYears(1)
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/policies", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CreatePolicy_WithInvalidCoverageAmount_ReturnsBadRequest400()
    {
        // Arrange
        var customer = await CreateUserAsync("Cust Bad Cov", $"cust_{Guid.NewGuid():N}@example.com", UserRole.Customer);
        var admin = await CreateUserAsync("Admin User", $"admin_{Guid.NewGuid():N}@example.com", UserRole.Admin);
        var adminToken = TestTokenHelper.CreateToken(admin.Id, role: "Admin");

        var request = new CreatePolicyRequestDto
        {
            PolicyNumber = "POL-BADCOV",
            CustomerId = customer.Id,
            PolicyType = PolicyType.Home,
            CoverageAmount = -500m, // Invalid coverage
            StartDate = DateTimeOffset.UtcNow,
            EndDate = DateTimeOffset.UtcNow.AddYears(1)
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/policies")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Act
        var response = await _client.SendAsync(httpRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CreatePolicy_WithDuplicatePolicyNumber_ReturnsConflict409()
    {
        // Arrange
        var customer = await CreateUserAsync("Cust Dup", $"cust_{Guid.NewGuid():N}@example.com", UserRole.Customer);
        var admin = await CreateUserAsync("Admin User", $"admin_{Guid.NewGuid():N}@example.com", UserRole.Admin);
        var adminToken = TestTokenHelper.CreateToken(admin.Id, role: "Admin");

        var dupNumber = $"POL-DUP-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant();
        var request = new CreatePolicyRequestDto
        {
            PolicyNumber = dupNumber,
            CustomerId = customer.Id,
            PolicyType = PolicyType.Auto,
            CoverageAmount = 10000m,
            StartDate = DateTimeOffset.UtcNow,
            EndDate = DateTimeOffset.UtcNow.AddYears(1)
        };

        using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/policies") { Content = JsonContent.Create(request) };
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var res1 = await _client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Act
        using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/policies") { Content = JsonContent.Create(request) };
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var res2 = await _client.SendAsync(req2);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
    }

    [Fact]
    public async Task Customer_GetPolicies_ReturnsOnlyOwnPolicies()
    {
        // Arrange
        var customer1 = await CreateUserAsync("Cust One", $"c1_{Guid.NewGuid():N}@example.com", UserRole.Customer);
        var customer2 = await CreateUserAsync("Cust Two", $"c2_{Guid.NewGuid():N}@example.com", UserRole.Customer);
        var admin = await CreateUserAsync("Admin User", $"admin_{Guid.NewGuid():N}@example.com", UserRole.Admin);
        var adminToken = TestTokenHelper.CreateToken(admin.Id, role: "Admin");

        var pol1Number = $"POL-C1-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant();
        var pol2Number = $"POL-C2-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant();

        // Admin creates policy for customer 1 and customer 2
        using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/policies")
        {
            Content = JsonContent.Create(new CreatePolicyRequestDto
            {
                PolicyNumber = pol1Number,
                CustomerId = customer1.Id,
                PolicyType = PolicyType.Auto,
                CoverageAmount = 15000m,
                StartDate = DateTimeOffset.UtcNow,
                EndDate = DateTimeOffset.UtcNow.AddYears(1)
            })
        };
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        await _client.SendAsync(req1);

        using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/policies")
        {
            Content = JsonContent.Create(new CreatePolicyRequestDto
            {
                PolicyNumber = pol2Number,
                CustomerId = customer2.Id,
                PolicyType = PolicyType.Home,
                CoverageAmount = 30000m,
                StartDate = DateTimeOffset.UtcNow,
                EndDate = DateTimeOffset.UtcNow.AddYears(1)
            })
        };
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        await _client.SendAsync(req2);

        // Customer 1 requests policies
        var customer1Token = TestTokenHelper.CreateToken(customer1.Id, role: "Customer");
        using var getReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/policies");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customer1Token);

        // Act
        var response = await _client.SendAsync(getReq);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var policies = JsonSerializer.Deserialize<List<PolicyResponseDto>>(content, JsonOptions);

        Assert.NotNull(policies);
        Assert.Contains(policies, p => p.PolicyNumber == pol1Number);
        Assert.DoesNotContain(policies, p => p.PolicyNumber == pol2Number);
    }

    [Fact]
    public async Task Customer_GetPolicyById_ForOtherCustomerPolicy_ReturnsNotFound404()
    {
        // Arrange
        var customer1 = await CreateUserAsync("Cust One", $"c1_{Guid.NewGuid():N}@example.com", UserRole.Customer);
        var customer2 = await CreateUserAsync("Cust Two", $"c2_{Guid.NewGuid():N}@example.com", UserRole.Customer);
        var admin = await CreateUserAsync("Admin User", $"admin_{Guid.NewGuid():N}@example.com", UserRole.Admin);
        var adminToken = TestTokenHelper.CreateToken(admin.Id, role: "Admin");

        var pol2Number = $"POL-C2-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant();
        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/policies")
        {
            Content = JsonContent.Create(new CreatePolicyRequestDto
            {
                PolicyNumber = pol2Number,
                CustomerId = customer2.Id,
                PolicyType = PolicyType.Home,
                CoverageAmount = 30000m,
                StartDate = DateTimeOffset.UtcNow,
                EndDate = DateTimeOffset.UtcNow.AddYears(1)
            })
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var createRes = await _client.SendAsync(createReq);
        var created = JsonSerializer.Deserialize<PolicyResponseDto>(await createRes.Content.ReadAsStringAsync(), JsonOptions);
        Assert.NotNull(created);

        // Customer 1 tries to GET customer 2's policy
        var customer1Token = TestTokenHelper.CreateToken(customer1.Id, role: "Customer");
        using var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/policies/{created.Id}");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customer1Token);

        // Act
        var response = await _client.SendAsync(getReq);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
