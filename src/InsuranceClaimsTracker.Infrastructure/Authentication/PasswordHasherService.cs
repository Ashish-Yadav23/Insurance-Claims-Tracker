using InsuranceClaimsTracker.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;
using AppPasswordResult = InsuranceClaimsTracker.Application.Common.Models.PasswordVerificationResult;
using IdentityResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace InsuranceClaimsTracker.Infrastructure.Authentication;

public class PasswordHasherService : IPasswordHasherService
{
    private readonly PasswordHasher<object> _hasher = new();
    private static readonly object DummyUser = new();

    public string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        return _hasher.HashPassword(DummyUser, password);
    }

    public AppPasswordResult VerifyHashedPassword(string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword) || string.IsNullOrEmpty(providedPassword))
        {
            return AppPasswordResult.Failed;
        }

        var result = _hasher.VerifyHashedPassword(DummyUser, hashedPassword, providedPassword);

        return result switch
        {
            IdentityResult.Success => AppPasswordResult.Success,
            IdentityResult.SuccessRehashNeeded => AppPasswordResult.SuccessRehashNeeded,
            _ => AppPasswordResult.Failed
        };
    }
}
