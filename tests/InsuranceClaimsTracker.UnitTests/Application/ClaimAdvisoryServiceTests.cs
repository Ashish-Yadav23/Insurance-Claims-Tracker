using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Application.Services;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using InsuranceClaimsTracker.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InsuranceClaimsTracker.UnitTests.Application;

public class ClaimAdvisoryServiceTests
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TestCurrentUserService _currentUserService;
    private readonly TestLlmClient _llmClient;
    private readonly ClaimAdvisoryService _advisoryService;

    public ClaimAdvisoryServiceTests()
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
        _llmClient = new TestLlmClient();
        _advisoryService = new ClaimAdvisoryService(
            _dbContext,
            _llmClient,
            _currentUserService,
            NullLogger<ClaimAdvisoryService>.Instance);
    }

    private async Task<(User customer, Policy policy, Claim claim)> SeedClaimScenarioAsync(
        string description = "Heavy rain caused water damage to the basement ceiling.",
        PolicyType policyType = PolicyType.Home,
        decimal claimAmount = 4500m)
    {
        var customer = new User("Jane Customer", $"jane_{Guid.NewGuid():N}@example.com", "hash", UserRole.Customer);
        _dbContext.Users.Add(customer);
        await _dbContext.SaveChangesAsync();

        var policy = new Policy(
            $"POL-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant(),
            customer.Id,
            policyType,
            25000m,
            DateTimeOffset.UtcNow.AddMonths(-3),
            DateTimeOffset.UtcNow.AddMonths(9));
        _dbContext.Policies.Add(policy);
        await _dbContext.SaveChangesAsync();

        var claim = new Claim(policy.Id, claimAmount, DateTimeOffset.UtcNow.AddDays(-2), description)
        {
            Status = ClaimStatus.Submitted,
            RowVersion = [0x01, 0x02, 0x03, 0x04]
        };
        _dbContext.Claims.Add(claim);
        await _dbContext.SaveChangesAsync();

        return (customer, policy, claim);
    }

    // =========================================================================
    // 1. Classification & Summarization Scenarios
    // =========================================================================

    [Fact]
    public async Task GetClaimAdvisory_WaterDamage_ReturnsExpectedCategoryAndSummary()
    {
        var (_, _, claim) = await SeedClaimScenarioAsync("Burst pipe flooded kitchen hardwood floors.", PolicyType.Home);
        _llmClient.ResponseToReturn = LlmAdvisoryResult.Success(
            "{\"category\": \"WaterDamage\", \"summary\": \"Burst plumbing line caused flooding across kitchen flooring.\", \"suggestedPriority\": \"Medium\"}");

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal("WaterDamage", result.Value.Category);
        Assert.Equal("Burst plumbing line caused flooding across kitchen flooring.", result.Value.Summary);
        Assert.Equal("Medium", result.Value.SuggestedPriority);

        // Verify sanitized request payload sent to LLM contains no PII
        Assert.NotNull(_llmClient.LastRequest);
        Assert.Equal("Home", _llmClient.LastRequest.PolicyType);
        Assert.Equal("Burst pipe flooded kitchen hardwood floors.", _llmClient.LastRequest.Description);
    }

    [Fact]
    public async Task GetClaimAdvisory_TheftClassification_ReturnsTheftAndHighPriority()
    {
        var (_, _, claim) = await SeedClaimScenarioAsync("Vehicle broken into, catalytic converter and sound system stolen.", PolicyType.Auto);
        _llmClient.ResponseToReturn = LlmAdvisoryResult.Success(
            "{\"category\": \"Theft\", \"summary\": \"Forced vehicle entry resulting in stolen catalytic converter and audio equipment.\", \"suggestedPriority\": \"High\"}");

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Theft", result.Value.Category);
        Assert.Equal("High", result.Value.SuggestedPriority);
    }

    [Fact]
    public async Task GetClaimAdvisory_MedicalClassification_ReturnsMedicalAndPriority()
    {
        var (_, _, claim) = await SeedClaimScenarioAsync("Emergency appendectomy surgery and three-day hospital stay.", PolicyType.Health);
        _llmClient.ResponseToReturn = LlmAdvisoryResult.Success(
            "{\"category\": \"Medical\", \"summary\": \"Emergency surgical operation and multi-day inpatient hospital recovery.\", \"suggestedPriority\": \"High\"}");

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Medical", result.Value.Category);
        Assert.Equal("High", result.Value.SuggestedPriority);
    }

    [Fact]
    public async Task GetClaimAdvisory_AutoAccidentClassification_ReturnsAutoAccident()
    {
        var (_, _, claim) = await SeedClaimScenarioAsync("Rear-ended at traffic signal causing bumper and trunk collapse.", PolicyType.Auto);
        _llmClient.ResponseToReturn = LlmAdvisoryResult.Success(
            "{\"category\": \"AutoAccident\", \"summary\": \"Rear-end collision at intersection with structural rear trunk damage.\", \"suggestedPriority\": \"Medium\"}");

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("AutoAccident", result.Value.Category);
        Assert.Equal("Medium", result.Value.SuggestedPriority);
    }

    [Fact]
    public async Task GetClaimAdvisory_WithMarkdownCodeFence_ExtractsAndParsesJsonSuccessfully()
    {
        var (_, _, claim) = await SeedClaimScenarioAsync();
        _llmClient.ResponseToReturn = LlmAdvisoryResult.Success(
            "```json\n{\"category\": \"PropertyDamage\", \"summary\": \"Fallen tree branch damaged roof shingles.\", \"suggestedPriority\": \"Low\"}\n```");

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("PropertyDamage", result.Value.Category);
        Assert.Equal("Low", result.Value.SuggestedPriority);
    }

    // =========================================================================
    // 2. Validation & Defensive Checks on Model Output
    // =========================================================================

    [Fact]
    public async Task GetClaimAdvisory_WhenProviderReturnsInvalidJson_ReturnsServiceUnavailable503()
    {
        var (_, _, claim) = await SeedClaimScenarioAsync();
        _llmClient.ResponseToReturn = LlmAdvisoryResult.Success("Not valid JSON: { broken");

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("malformed", result.ErrorMessage);
    }

    [Fact]
    public async Task GetClaimAdvisory_WhenProviderReturnsUnsupportedCategory_Returns503()
    {
        var (_, _, claim) = await SeedClaimScenarioAsync();
        _llmClient.ResponseToReturn = LlmAdvisoryResult.Success(
            "{\"category\": \"AlienInvasion\", \"summary\": \"Unidentified aerial phenomenon.\", \"suggestedPriority\": \"Low\"}");

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("unsupported category", result.ErrorMessage);
    }

    [Fact]
    public async Task GetClaimAdvisory_WhenProviderReturnsUnsupportedPriority_Returns503()
    {
        var (_, _, claim) = await SeedClaimScenarioAsync();
        _llmClient.ResponseToReturn = LlmAdvisoryResult.Success(
            "{\"category\": \"WaterDamage\", \"summary\": \"Ceiling leak.\", \"suggestedPriority\": \"UrgentCritical\"}");

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("unsupported priority", result.ErrorMessage);
    }

    [Fact]
    public async Task GetClaimAdvisory_WhenProviderReturnsOversizedSummary_Returns503()
    {
        var (_, _, claim) = await SeedClaimScenarioAsync();
        var massiveSummary = new string('A', 1500);
        _llmClient.ResponseToReturn = LlmAdvisoryResult.Success(
            $"{{\"category\": \"WaterDamage\", \"summary\": \"{massiveSummary}\", \"suggestedPriority\": \"Medium\"}}");

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("oversized summary", result.ErrorMessage);
    }

    [Fact]
    public async Task GetClaimAdvisory_WhenClaimDescriptionIsEmpty_ReturnsBadRequest400()
    {
        var (_, _, claim) = await SeedClaimScenarioAsync();
        claim.Description = "   ";
        await _dbContext.SaveChangesAsync();

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("cannot be empty", result.ErrorMessage);
        Assert.Equal(0, _llmClient.CallCount);
    }

    // =========================================================================
    // 3. Resilience, Timeouts & Error Propagation
    // =========================================================================

    [Fact]
    public async Task GetClaimAdvisory_WhenProviderTimesOut_ReturnsServiceUnavailable503()
    {
        var (_, _, claim) = await SeedClaimScenarioAsync();
        _llmClient.ResponseToReturn = LlmAdvisoryResult.Failure("The AI advisory service request timed out.", 503);

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("timed out", result.ErrorMessage);
    }

    [Fact]
    public async Task GetClaimAdvisory_WhenProviderRateLimits429_Returns503WithSafeMessage()
    {
        var (_, _, claim) = await SeedClaimScenarioAsync();
        _llmClient.ResponseToReturn = LlmAdvisoryResult.Failure("The AI advisory service rate limit was exceeded. Please try again later.", 503);

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("rate limit was exceeded", result.ErrorMessage);
    }

    [Fact]
    public async Task GetClaimAdvisory_WhenProviderReturns5xx_Returns503ServiceUnavailable()
    {
        var (_, _, claim) = await SeedClaimScenarioAsync();
        _llmClient.ResponseToReturn = LlmAdvisoryResult.Failure("The AI advisory service is temporarily unavailable. Please try again later.", 503);

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("temporarily unavailable", result.ErrorMessage);
    }

    [Fact]
    public async Task GetClaimAdvisory_WhenCancellationTokenCancelled_ThrowsOperationCanceledException()
    {
        var (_, _, claim) = await SeedClaimScenarioAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _advisoryService.GetClaimAdvisoryAsync(claim.Id, cts.Token));
    }

    // =========================================================================
    // 4. Authorization, Resource Verification & Immutability
    // =========================================================================

    [Fact]
    public async Task GetClaimAdvisory_WhenUserIsCustomer_ReturnsForbidden403()
    {
        _currentUserService.UserRole = "Customer";
        var (_, _, claim) = await SeedClaimScenarioAsync();

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(0, _llmClient.CallCount);
    }

    [Fact]
    public async Task GetClaimAdvisory_WhenUserIsUnauthenticated_ReturnsUnauthorized401()
    {
        _currentUserService.IsAuthenticated = false;
        _currentUserService.UserId = null;
        var (_, _, claim) = await SeedClaimScenarioAsync();

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
        Assert.Equal(0, _llmClient.CallCount);
    }

    [Fact]
    public async Task GetClaimAdvisory_WhenClaimDoesNotExist_ReturnsNotFound404()
    {
        var result = await _advisoryService.GetClaimAdvisoryAsync(999999);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal(0, _llmClient.CallCount);
    }

    [Fact]
    public async Task GetClaimAdvisory_DoesNotMutateClaimOrAddClaimHistory()
    {
        var (_, _, claim) = await SeedClaimScenarioAsync("Minor parking lot dent.");
        var originalStatus = claim.Status;
        var originalApprovedAmount = claim.ApprovedAmount;
        var originalPaymentRef = claim.PaymentReference;
        var originalPaidAt = claim.PaidAt;
        var originalRowVersion = claim.RowVersion;

        _llmClient.ResponseToReturn = LlmAdvisoryResult.Success(
            "{\"category\": \"AutoAccident\", \"summary\": \"Parking lot bumper scrape.\", \"suggestedPriority\": \"Low\"}");

        var result = await _advisoryService.GetClaimAdvisoryAsync(claim.Id);

        Assert.True(result.IsSuccess);

        // Verify claim in database remains 100% untouched
        var claimInDb = await _dbContext.Claims.Include(c => c.History).FirstAsync(c => c.Id == claim.Id);
        Assert.Equal(originalStatus, claimInDb.Status);
        Assert.Equal(originalApprovedAmount, claimInDb.ApprovedAmount);
        Assert.Equal(originalPaymentRef, claimInDb.PaymentReference);
        Assert.Equal(originalPaidAt, claimInDb.PaidAt);
        Assert.Equal(originalRowVersion, claimInDb.RowVersion);
        Assert.Empty(claimInDb.History);
    }
}
