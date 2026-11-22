using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Application.DTOs.Claims;

namespace InsuranceClaimsTracker.Application.Common.Interfaces;

public interface IClaimService
{
    Task<ServiceResult<ClaimResponseDto>> SubmitClaimAsync(
        CreateClaimRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IEnumerable<ClaimResponseDto>>> GetClaimsAsync(
        int? policyId = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ClaimResponseDto>> GetClaimByIdAsync(
        int id,
        CancellationToken cancellationToken = default);
}
