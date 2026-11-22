using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Application.DTOs.Policies;

namespace InsuranceClaimsTracker.Application.Common.Interfaces;

public interface IPolicyService
{
    Task<ServiceResult<PolicyResponseDto>> CreatePolicyAsync(
        CreatePolicyRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IEnumerable<PolicyResponseDto>>> GetPoliciesAsync(
        int? customerId = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PolicyResponseDto>> GetPolicyByIdAsync(
        int id,
        CancellationToken cancellationToken = default);
}
