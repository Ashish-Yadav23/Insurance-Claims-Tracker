namespace InsuranceClaimsTracker.Application.Common.Interfaces;

public interface ICurrentUserService
{
    int? UserId { get; }
    string? UserRole { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin => string.Equals(UserRole, "Admin", StringComparison.OrdinalIgnoreCase);
    bool IsCustomer => string.Equals(UserRole, "Customer", StringComparison.OrdinalIgnoreCase);
}
