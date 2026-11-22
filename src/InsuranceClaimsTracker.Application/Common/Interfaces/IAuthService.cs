using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Application.DTOs.Auth;

namespace InsuranceClaimsTracker.Application.Common.Interfaces;

public interface IAuthService
{
    Task<AuthResult<UserProfileDto>> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default);
    Task<AuthResult<AuthResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task<UserProfileDto?> GetCurrentUserProfileAsync(int userId, CancellationToken cancellationToken = default);
}
