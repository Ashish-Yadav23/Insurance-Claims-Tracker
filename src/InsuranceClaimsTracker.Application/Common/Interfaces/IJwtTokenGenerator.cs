using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Domain.Entities;

namespace InsuranceClaimsTracker.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    GeneratedToken GenerateToken(User user);
}
