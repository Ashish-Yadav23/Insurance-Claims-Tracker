using InsuranceClaimsTracker.Application.DTOs.Policies;
using InsuranceClaimsTracker.Application.Services;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using InsuranceClaimsTracker.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InsuranceClaimsTracker.UnitTests.Application;

public class PolicyServiceTests
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TestCurrentUserService _currentUserService;
    private readonly PolicyService _policyService;

    public PolicyServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(dbOptions);
        _currentUserService = new TestCurrentUserService
        {
            UserId = 1,
            UserRole = "Admin",
            IsAuthenticated = true
        };

        _policyService = new PolicyService(_dbContext, _currentUserService);
    }

    [Fact]
    public async Task CreatePolicyAsync_AsAdmin_WithValidDetails_CreatesPolicy()
    {
        // Arrange
        var customer = new User("Customer One", "customer1@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var request = new CreatePolicyRequestDto
        {
            PolicyNumber = "POL-AUTO-001",
            CustomerId = customer.Id,
            PolicyType = PolicyType.Auto,
            CoverageAmount = 50000m,
            StartDate = DateTimeOffset.UtcNow.AddDays(-10),
            EndDate = DateTimeOffset.UtcNow.AddDays(355)
        };

        // Act
        var result = await _policyService.CreatePolicyAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(201, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal("POL-AUTO-001", result.Value.PolicyNumber);
        Assert.Equal(50000m, result.Value.CoverageAmount);
        Assert.Equal(customer.Id, result.Value.CustomerId);
        Assert.Equal("Customer One", result.Value.CustomerName);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task CreatePolicyAsync_AsCustomer_ReturnsForbidden()
    {
        // Arrange
        _currentUserService.UserId = 2;
        _currentUserService.UserRole = "Customer";

        var request = new CreatePolicyRequestDto
        {
            PolicyNumber = "POL-FORBIDDEN-01",
            CustomerId = 2,
            PolicyType = PolicyType.Auto,
            CoverageAmount = 10000m,
            StartDate = DateTimeOffset.UtcNow,
            EndDate = DateTimeOffset.UtcNow.AddYears(1)
        };

        // Act
        var result = await _policyService.CreatePolicyAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("Only administrators can create policies.", result.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task CreatePolicyAsync_WithInvalidCoverageAmount_ReturnsBadRequest(decimal invalidCoverage)
    {
        // Arrange
        var customer = new User("Customer One", "customer1@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var request = new CreatePolicyRequestDto
        {
            PolicyNumber = "POL-002",
            CustomerId = customer.Id,
            PolicyType = PolicyType.Home,
            CoverageAmount = invalidCoverage,
            StartDate = DateTimeOffset.UtcNow,
            EndDate = DateTimeOffset.UtcNow.AddYears(1)
        };

        // Act
        var result = await _policyService.CreatePolicyAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("Coverage amount must be greater than zero.", result.ErrorMessage);
    }

    [Fact]
    public async Task CreatePolicyAsync_WithEndDateBeforeStartDate_ReturnsBadRequest()
    {
        // Arrange
        var customer = new User("Customer One", "customer1@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var request = new CreatePolicyRequestDto
        {
            PolicyNumber = "POL-003",
            CustomerId = customer.Id,
            PolicyType = PolicyType.Home,
            CoverageAmount = 25000m,
            StartDate = DateTimeOffset.UtcNow.AddDays(10),
            EndDate = DateTimeOffset.UtcNow.AddDays(5) // End before start
        };

        // Act
        var result = await _policyService.CreatePolicyAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("Policy end date must be after start date.", result.ErrorMessage);
    }

    [Fact]
    public async Task CreatePolicyAsync_WithDuplicatePolicyNumber_ReturnsConflict()
    {
        // Arrange
        var customer = new User("Customer One", "customer1@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var existingPolicy = new Policy("POL-DUPLICATE-01", customer.Id, PolicyType.Auto, 30000m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        _dbContext.Policies.Add(existingPolicy);
        await _dbContext.SaveChangesAsync();

        var request = new CreatePolicyRequestDto
        {
            PolicyNumber = "pol-duplicate-01", // Case-insensitive check
            CustomerId = customer.Id,
            PolicyType = PolicyType.Auto,
            CoverageAmount = 40000m,
            StartDate = DateTimeOffset.UtcNow,
            EndDate = DateTimeOffset.UtcNow.AddYears(1)
        };

        // Act
        var result = await _policyService.CreatePolicyAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("already exists", result.ErrorMessage);
    }

    [Fact]
    public async Task CreatePolicyAsync_WithNonExistentCustomerId_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreatePolicyRequestDto
        {
            PolicyNumber = "POL-NO-CUST",
            CustomerId = 9999,
            PolicyType = PolicyType.Health,
            CoverageAmount = 15000m,
            StartDate = DateTimeOffset.UtcNow,
            EndDate = DateTimeOffset.UtcNow.AddYears(1)
        };

        // Act
        var result = await _policyService.CreatePolicyAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("does not exist", result.ErrorMessage);
    }

    [Fact]
    public async Task CreatePolicyAsync_WithAdminAsTargetCustomer_ReturnsBadRequest()
    {
        // Arrange
        var adminUser = new User("Admin Target", "admin2@example.com", "hash", UserRole.Admin);
        _dbContext.Users.Add(adminUser);
        await _dbContext.SaveChangesAsync();

        var request = new CreatePolicyRequestDto
        {
            PolicyNumber = "POL-ADMIN-TARGET",
            CustomerId = adminUser.Id,
            PolicyType = PolicyType.Health,
            CoverageAmount = 15000m,
            StartDate = DateTimeOffset.UtcNow,
            EndDate = DateTimeOffset.UtcNow.AddYears(1)
        };

        // Act
        var result = await _policyService.CreatePolicyAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("Policies can only be assigned to accounts with the Customer role.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetPoliciesAsync_AsCustomer_ReturnsOnlyCustomerPolicies()
    {
        // Arrange
        var customer1 = new User("Customer 1", "c1@example.com", "hash", UserRole.Customer);
        var customer2 = new User("Customer 2", "c2@example.com", "hash", UserRole.Customer);
        _dbContext.Users.AddRange(customer1, customer2);
        await _dbContext.SaveChangesAsync();

        var policy1 = new Policy("POL-C1", customer1.Id, PolicyType.Auto, 20000m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        var policy2 = new Policy("POL-C2", customer2.Id, PolicyType.Home, 50000m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        _dbContext.Policies.AddRange(policy1, policy2);
        await _dbContext.SaveChangesAsync();

        _currentUserService.UserId = customer1.Id;
        _currentUserService.UserRole = "Customer";

        // Act
        var result = await _policyService.GetPoliciesAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        var list = result.Value.ToList();
        Assert.Single(list);
        Assert.Equal("POL-C1", list[0].PolicyNumber);
    }

    [Fact]
    public async Task GetPoliciesAsync_AsAdmin_ReturnsAllPolicies()
    {
        // Arrange
        var customer1 = new User("Customer 1", "c1@example.com", "hash", UserRole.Customer);
        var customer2 = new User("Customer 2", "c2@example.com", "hash", UserRole.Customer);
        _dbContext.Users.AddRange(customer1, customer2);
        await _dbContext.SaveChangesAsync();

        var policy1 = new Policy("POL-A1", customer1.Id, PolicyType.Auto, 20000m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        var policy2 = new Policy("POL-A2", customer2.Id, PolicyType.Home, 50000m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        _dbContext.Policies.AddRange(policy1, policy2);
        await _dbContext.SaveChangesAsync();

        _currentUserService.UserId = 99;
        _currentUserService.UserRole = "Admin";

        // Act
        var result = await _policyService.GetPoliciesAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Count());
    }

    [Fact]
    public async Task GetPolicyByIdAsync_AsCustomer_ForOwnedPolicy_ReturnsPolicy()
    {
        // Arrange
        var customer = new User("Customer 1", "c1@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var policy = new Policy("POL-OWNED", customer.Id, PolicyType.Auto, 20000m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        _dbContext.Policies.Add(policy);
        await _dbContext.SaveChangesAsync();

        _currentUserService.UserId = customer.Id;
        _currentUserService.UserRole = "Customer";

        // Act
        var result = await _policyService.GetPolicyByIdAsync(policy.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal("POL-OWNED", result.Value.PolicyNumber);
    }

    [Fact]
    public async Task GetPolicyByIdAsync_AsCustomer_ForOtherCustomerPolicy_ReturnsNotFound()
    {
        // Arrange
        var customer1 = new User("Customer 1", "c1@example.com", "hash", UserRole.Customer);
        var customer2 = new User("Customer 2", "c2@example.com", "hash", UserRole.Customer);
        _dbContext.Users.AddRange(customer1, customer2);
        await _dbContext.SaveChangesAsync();

        var policyOfCustomer2 = new Policy("POL-C2-PRIVATE", customer2.Id, PolicyType.Auto, 20000m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        _dbContext.Policies.Add(policyOfCustomer2);
        await _dbContext.SaveChangesAsync();

        // Customer 1 tries to access Customer 2's policy
        _currentUserService.UserId = customer1.Id;
        _currentUserService.UserRole = "Customer";

        // Act
        var result = await _policyService.GetPolicyByIdAsync(policyOfCustomer2.Id);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("Policy not found.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetPolicyByIdAsync_AsAdmin_ForAnyPolicy_ReturnsPolicy()
    {
        // Arrange
        var customer = new User("Customer 1", "c1@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var policy = new Policy("POL-ANY", customer.Id, PolicyType.Life, 100000m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        _dbContext.Policies.Add(policy);
        await _dbContext.SaveChangesAsync();

        _currentUserService.UserId = 99;
        _currentUserService.UserRole = "Admin";

        // Act
        var result = await _policyService.GetPolicyByIdAsync(policy.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal("POL-ANY", result.Value.PolicyNumber);
    }
}
