using InsuranceClaimsTracker.Application.Common.Models;

namespace InsuranceClaimsTracker.Application.Common.Interfaces;

public interface ILlmClient
{
    Task<LlmAdvisoryResult> GenerateClaimAdvisoryAsync(
        LlmAdvisoryRequest request,
        CancellationToken cancellationToken = default);
}
