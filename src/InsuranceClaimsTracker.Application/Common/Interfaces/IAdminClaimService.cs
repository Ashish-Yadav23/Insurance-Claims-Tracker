using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Application.DTOs.Admin;
using InsuranceClaimsTracker.Application.DTOs.Claims;

namespace InsuranceClaimsTracker.Application.Common.Interfaces;

public interface IAdminClaimService
{
    Task<ServiceResult<ClaimResponseDto>> StartReviewAsync(
        int claimId,
        StartReviewRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ClaimResponseDto>> ProcessDecisionAsync(
        int claimId,
        ClaimDecisionRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ClaimResponseDto>> RecordPayoutAsync(
        int claimId,
        ClaimPayoutRequestDto request,
        CancellationToken cancellationToken = default);
}
