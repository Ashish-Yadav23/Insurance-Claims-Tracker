using InsuranceClaimsTracker.Application.Common.Interfaces;

namespace InsuranceClaimsTracker.UnitTests.TestDoubles;

public class TestCurrentUserService : ICurrentUserService
{
    public int? UserId { get; set; }
    public string? UserRole { get; set; }
    public string? Email { get; set; }
    public bool IsAuthenticated { get; set; } = true;
}
