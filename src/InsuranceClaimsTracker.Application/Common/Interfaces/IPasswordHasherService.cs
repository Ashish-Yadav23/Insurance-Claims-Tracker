using InsuranceClaimsTracker.Application.Common.Models;

namespace InsuranceClaimsTracker.Application.Common.Interfaces;

public interface IPasswordHasherService
{
    string HashPassword(string password);
    PasswordVerificationResult VerifyHashedPassword(string hashedPassword, string providedPassword);
}
