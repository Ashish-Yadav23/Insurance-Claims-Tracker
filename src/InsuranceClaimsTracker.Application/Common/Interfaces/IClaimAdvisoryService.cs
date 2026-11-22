using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Application.DTOs.Admin;

namespace InsuranceClaimsTracker.Application.Common.Interfaces;

public interface IClaimAdvisoryService
{
    Task<ServiceResult<ClaimAdvisoryResponseDto>> GetClaimAdvisoryAsync(
        int claimId,
        CancellationToken cancellationToken = default);
}
