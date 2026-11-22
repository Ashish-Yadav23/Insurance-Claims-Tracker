using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Application.Common.Models;

namespace InsuranceClaimsTracker.IntegrationTests;

public class MockIntegrationLlmClient : ILlmClient
{
    public LlmAdvisoryResult ResultToReturn { get; set; } = LlmAdvisoryResult.Success(
        "{\"category\": \"WaterDamage\", \"summary\": \"Burst plumbing fixture flooded hardwood flooring.\", \"suggestedPriority\": \"Medium\"}");

    public int CallCount { get; private set; }
    public LlmAdvisoryRequest? LastRequest { get; private set; }

    public Task<LlmAdvisoryResult> GenerateClaimAdvisoryAsync(
        LlmAdvisoryRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastRequest = request;
        return Task.FromResult(ResultToReturn);
    }
}
