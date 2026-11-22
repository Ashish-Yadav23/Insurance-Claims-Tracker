using InsuranceClaimsTracker.Application.DTOs.Claims;
using InsuranceClaimsTracker.Application.Services;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using InsuranceClaimsTracker.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InsuranceClaimsTracker.UnitTests.Application;

public class ClaimServiceTests
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TestCurrentUserService _currentUserService;
    private readonly ClaimService _claimService;

    public ClaimServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(dbOptions);
        _currentUserService = new TestCurrentUserService
        {
            UserId = 1,
            UserRole = "Customer",
            IsAuthenticated = true
        };

        _claimService = new ClaimService(_dbContext, _currentUserService);
    }

    [Fact]
    public async Task SubmitClaimAsync_WithValidCustomerAndPolicy_CreatesClaimAndInitialHistory()
    {
        // Arrange
        var customer = new User("Alice", "alice@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var policy = new Policy("POL-VALID-01", customer.Id, PolicyType.Auto, 20000m,
            DateTimeOffset.UtcNow.AddMonths(-6), DateTimeOffset.UtcNow.AddMonths(6));
        _dbContext.Policies.Add(policy);
        await _dbContext.SaveChangesAsync();

        _currentUserService.UserId = customer.Id;
        _currentUserService.UserRole = "Customer";

        var request = new CreateClaimRequestDto
        {
            PolicyId = policy.Id,
            ClaimAmount = 3500.50m,
            IncidentDate = DateTimeOffset.UtcNow.AddDays(-5),
            Description = "Windshield crack from road gravel on the highway."
        };

        // Act
        var result = await _claimService.SubmitClaimAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(201, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal("Submitted", result.Value.Status);
        Assert.Equal(3500.50m, result.Value.ClaimAmount);
        Assert.Equal(policy.Id, result.Value.PolicyId);
        Assert.Equal("POL-VALID-01", result.Value.PolicyNumber);
        Assert.Equal(customer.Id, result.Value.CustomerId);

        // Verify initial ClaimHistory
        Assert.Single(result.Value.History);
        var initialHistory = result.Value.History[0];
        Assert.Null(initialHistory.PreviousStatus);
        Assert.Equal("Submitted", initialHistory.NewStatus);
        Assert.Equal(customer.Id, initialHistory.ChangedById);

        // Verify database persistence
        var savedClaim = await _dbContext.Claims
            .Include(c => c.History)
            .FirstOrDefaultAsync(c => c.Id == result.Value.Id);
        Assert.NotNull(savedClaim);
        Assert.Equal(ClaimStatus.Submitted, savedClaim.Status);
        Assert.Single(savedClaim.History);
        Assert.Equal(customer.Id, savedClaim.History.First().ChangedById);
    }

    [Fact]
    public async Task SubmitClaimAsync_AsAdmin_ReturnsForbidden()
    {
        // Arrange
        _currentUserService.UserId = 99;
        _currentUserService.UserRole = "Admin";

        var request = new CreateClaimRequestDto
        {
            PolicyId = 1,
            ClaimAmount = 500m,
            IncidentDate = DateTimeOffset.UtcNow.AddDays(-1),
            Description = "Admin attempting to submit claim."
        };

        // Act
        var result = await _claimService.SubmitClaimAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("Only customers can submit claims.", result.ErrorMessage);
    }

    [Fact]
    public async Task SubmitClaimAsync_AgainstAnotherCustomerPolicy_ReturnsNotFound()
    {
        // Arrange
        var customer1 = new User("Customer 1", "c1@example.com", "hash", UserRole.Customer);
        var customer2 = new User("Customer 2", "c2@example.com", "hash", UserRole.Customer);
        _dbContext.Users.AddRange(customer1, customer2);
        await _dbContext.SaveChangesAsync();

        var policyOfCustomer2 = new Policy("POL-C2", customer2.Id, PolicyType.Home, 50000m,
            DateTimeOffset.UtcNow.AddMonths(-6), DateTimeOffset.UtcNow.AddMonths(6));
        _dbContext.Policies.Add(policyOfCustomer2);
        await _dbContext.SaveChangesAsync();

        // Customer 1 tries to submit against Customer 2's policy
        _currentUserService.UserId = customer1.Id;
        _currentUserService.UserRole = "Customer";

        var request = new CreateClaimRequestDto
        {
            PolicyId = policyOfCustomer2.Id,
            ClaimAmount = 1000m,
            IncidentDate = DateTimeOffset.UtcNow.AddDays(-2),
            Description = "Claim against someone else's policy."
        };

        // Act
        var result = await _claimService.SubmitClaimAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("Policy not found.", result.ErrorMessage);
    }

    [Fact]
    public async Task SubmitClaimAsync_AgainstInactivePolicy_ReturnsBadRequest()
    {
        // Arrange
        var customer = new User("Customer", "c@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var inactivePolicy = new Policy("POL-INACTIVE", customer.Id, PolicyType.Auto, 10000m,
            DateTimeOffset.UtcNow.AddMonths(-6), DateTimeOffset.UtcNow.AddMonths(6))
        {
            IsActive = false
        };
        _dbContext.Policies.Add(inactivePolicy);
        await _dbContext.SaveChangesAsync();

        _currentUserService.UserId = customer.Id;
        _currentUserService.UserRole = "Customer";

        var request = new CreateClaimRequestDto
        {
            PolicyId = inactivePolicy.Id,
            ClaimAmount = 500m,
            IncidentDate = DateTimeOffset.UtcNow.AddDays(-2),
            Description = "Claim against cancelled policy."
        };

        // Act
        var result = await _claimService.SubmitClaimAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("inactive policy", result.ErrorMessage);
    }

    [Fact]
    public async Task SubmitClaimAsync_WithIncidentDateBeforePolicyStartDate_ReturnsBadRequest()
    {
        // Arrange
        var customer = new User("Customer", "c@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var policy = new Policy("POL-DATES", customer.Id, PolicyType.Auto, 10000m,
            DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddMonths(6));
        _dbContext.Policies.Add(policy);
        await _dbContext.SaveChangesAsync();

        _currentUserService.UserId = customer.Id;
        _currentUserService.UserRole = "Customer";

        var request = new CreateClaimRequestDto
        {
            PolicyId = policy.Id,
            ClaimAmount = 500m,
            IncidentDate = DateTimeOffset.UtcNow.AddDays(-15), // Before start date
            Description = "Incident occurred before coverage began."
        };

        // Act
        var result = await _claimService.SubmitClaimAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("must fall within policy coverage dates", result.ErrorMessage);
    }

    [Fact]
    public async Task SubmitClaimAsync_WithIncidentDateInFuture_ReturnsBadRequest()
    {
        // Arrange
        var customer = new User("Customer", "c@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var policy = new Policy("POL-FUTURE", customer.Id, PolicyType.Auto, 10000m,
            DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(6));
        _dbContext.Policies.Add(policy);
        await _dbContext.SaveChangesAsync();

        _currentUserService.UserId = customer.Id;
        _currentUserService.UserRole = "Customer";

        var request = new CreateClaimRequestDto
        {
            PolicyId = policy.Id,
            ClaimAmount = 500m,
            IncidentDate = DateTimeOffset.UtcNow.AddDays(5), // Future date
            Description = "Incident predicting future damage."
        };

        // Act
        var result = await _claimService.SubmitClaimAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("Incident date cannot be in the future.", result.ErrorMessage);
    }

    [Fact]
    public async Task SubmitClaimAsync_WithAmountExceedingPolicyCoverage_ReturnsBadRequest()
    {
        // Arrange
        var customer = new User("Customer", "c@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var policy = new Policy("POL-LIMIT", customer.Id, PolicyType.Auto, 5000m, // Limit $5000
            DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(6));
        _dbContext.Policies.Add(policy);
        await _dbContext.SaveChangesAsync();

        _currentUserService.UserId = customer.Id;
        _currentUserService.UserRole = "Customer";

        var request = new CreateClaimRequestDto
        {
            PolicyId = policy.Id,
            ClaimAmount = 5001m, // Exceeds limit
            IncidentDate = DateTimeOffset.UtcNow.AddDays(-2),
            Description = "Amount exceeds policy coverage."
        };

        // Act
        var result = await _claimService.SubmitClaimAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("cannot exceed policy coverage amount", result.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public async Task SubmitClaimAsync_WithZeroOrNegativeAmount_ReturnsBadRequest(decimal invalidAmount)
    {
        // Arrange
        var customer = new User("Customer", "c@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var policy = new Policy("POL-AMT", customer.Id, PolicyType.Auto, 10000m,
            DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(6));
        _dbContext.Policies.Add(policy);
        await _dbContext.SaveChangesAsync();

        _currentUserService.UserId = customer.Id;
        _currentUserService.UserRole = "Customer";

        var request = new CreateClaimRequestDto
        {
            PolicyId = policy.Id,
            ClaimAmount = invalidAmount,
            IncidentDate = DateTimeOffset.UtcNow.AddDays(-2),
            Description = "Invalid amount claim."
        };

        // Act
        var result = await _claimService.SubmitClaimAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("Claim amount must be greater than zero.", result.ErrorMessage);
    }

    [Fact]
    public async Task SubmitClaimAsync_WithEmptyDescription_ReturnsBadRequest()
    {
        // Arrange
        var customer = new User("Customer", "c@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var policy = new Policy("POL-DESC", customer.Id, PolicyType.Auto, 10000m,
            DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(6));
        _dbContext.Policies.Add(policy);
        await _dbContext.SaveChangesAsync();

        _currentUserService.UserId = customer.Id;
        _currentUserService.UserRole = "Customer";

        var request = new CreateClaimRequestDto
        {
            PolicyId = policy.Id,
            ClaimAmount = 100m,
            IncidentDate = DateTimeOffset.UtcNow.AddDays(-2),
            Description = "   " // Whitespace
        };

        // Act
        var result = await _claimService.SubmitClaimAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("Description is required.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetClaimsAsync_AsCustomer_ReturnsOnlyOwnClaims()
    {
        // Arrange
        var customer1 = new User("Customer 1", "c1@example.com", "hash", UserRole.Customer);
        var customer2 = new User("Customer 2", "c2@example.com", "hash", UserRole.Customer);
        _dbContext.Users.AddRange(customer1, customer2);
        await _dbContext.SaveChangesAsync();

        var policy1 = new Policy("POL-C1", customer1.Id, PolicyType.Auto, 10000m, DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(6));
        var policy2 = new Policy("POL-C2", customer2.Id, PolicyType.Home, 10000m, DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(6));
        _dbContext.Policies.AddRange(policy1, policy2);
        await _dbContext.SaveChangesAsync();

        var claim1 = new Claim(policy1.Id, 500m, DateTimeOffset.UtcNow.AddDays(-3), "Claim 1");
        var claim2 = new Claim(policy2.Id, 800m, DateTimeOffset.UtcNow.AddDays(-3), "Claim 2");
        _dbContext.Claims.AddRange(claim1, claim2);
        await _dbContext.SaveChangesAsync();

        _currentUserService.UserId = customer1.Id;
        _currentUserService.UserRole = "Customer";

        // Act
        var result = await _claimService.GetClaimsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        var list = result.Value.ToList();
        Assert.Single(list);
        Assert.Equal(claim1.Id, list[0].Id);
    }

    [Fact]
    public async Task GetClaimsAsync_AsAdmin_ReturnsAllClaims()
    {
        // Arrange
        var customer1 = new User("Customer 1", "c1@example.com", "hash", UserRole.Customer);
        var customer2 = new User("Customer 2", "c2@example.com", "hash", UserRole.Customer);
        _dbContext.Users.AddRange(customer1, customer2);
        await _dbContext.SaveChangesAsync();

        var policy1 = new Policy("POL-C1", customer1.Id, PolicyType.Auto, 10000m, DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(6));
        var policy2 = new Policy("POL-C2", customer2.Id, PolicyType.Home, 10000m, DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(6));
        _dbContext.Policies.AddRange(policy1, policy2);
        await _dbContext.SaveChangesAsync();

        var claim1 = new Claim(policy1.Id, 500m, DateTimeOffset.UtcNow.AddDays(-3), "Claim 1");
        var claim2 = new Claim(policy2.Id, 800m, DateTimeOffset.UtcNow.AddDays(-3), "Claim 2");
        _dbContext.Claims.AddRange(claim1, claim2);
        await _dbContext.SaveChangesAsync();

        _currentUserService.UserId = 99;
        _currentUserService.UserRole = "Admin";

        // Act
        var result = await _claimService.GetClaimsAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Count());
    }

    [Fact]
    public async Task GetClaimByIdAsync_AsCustomer_ForOwnedClaim_ReturnsClaimWithHistory()
    {
        // Arrange
        var customer = new User("Customer 1", "c1@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var policy = new Policy("POL-OWNED", customer.Id, PolicyType.Auto, 10000m, DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(6));
        _dbContext.Policies.Add(policy);
        await _dbContext.SaveChangesAsync();

        var claim = new Claim(policy.Id, 500m, DateTimeOffset.UtcNow.AddDays(-3), "Claim Owned");
        var history = new ClaimHistory(0, null, ClaimStatus.Submitted, customer.Id);
        claim.History.Add(history);
        _dbContext.Claims.Add(claim);
        await _dbContext.SaveChangesAsync();

        _currentUserService.UserId = customer.Id;
        _currentUserService.UserRole = "Customer";

        // Act
        var result = await _claimService.GetClaimByIdAsync(claim.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal(claim.Id, result.Value.Id);
        Assert.Single(result.Value.History);
        Assert.Equal("Submitted", result.Value.History[0].NewStatus);
    }

    [Fact]
    public async Task GetClaimByIdAsync_AsCustomer_ForOtherCustomerClaim_ReturnsNotFound()
    {
        // Arrange
        var customer1 = new User("Customer 1", "c1@example.com", "hash", UserRole.Customer);
        var customer2 = new User("Customer 2", "c2@example.com", "hash", UserRole.Customer);
        _dbContext.Users.AddRange(customer1, customer2);
        await _dbContext.SaveChangesAsync();

        var policyOfCustomer2 = new Policy("POL-C2", customer2.Id, PolicyType.Home, 10000m, DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(6));
        _dbContext.Policies.Add(policyOfCustomer2);
        await _dbContext.SaveChangesAsync();

        var claimOfCustomer2 = new Claim(policyOfCustomer2.Id, 750m, DateTimeOffset.UtcNow.AddDays(-3), "Private Claim");
        _dbContext.Claims.Add(claimOfCustomer2);
        await _dbContext.SaveChangesAsync();

        // Customer 1 tries to access Customer 2's claim
        _currentUserService.UserId = customer1.Id;
        _currentUserService.UserRole = "Customer";

        // Act
        var result = await _claimService.GetClaimByIdAsync(claimOfCustomer2.Id);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("Claim not found.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetClaimByIdAsync_AsAdmin_ForAnyClaim_ReturnsClaim()
    {
        // Arrange
        var customer = new User("Customer", "c@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var policy = new Policy("POL-C", customer.Id, PolicyType.Home, 10000m, DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(6));
        _dbContext.Policies.Add(policy);
        await _dbContext.SaveChangesAsync();

        var claim = new Claim(policy.Id, 750m, DateTimeOffset.UtcNow.AddDays(-3), "Claim Visible To Admin");
        _dbContext.Claims.Add(claim);
        await _dbContext.SaveChangesAsync();

        _currentUserService.UserId = 99;
        _currentUserService.UserRole = "Admin";

        // Act
        var result = await _claimService.GetClaimByIdAsync(claim.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal(claim.Id, result.Value.Id);
    }
}
