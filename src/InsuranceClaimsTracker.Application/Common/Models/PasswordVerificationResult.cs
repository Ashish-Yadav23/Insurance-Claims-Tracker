namespace InsuranceClaimsTracker.Application.Common.Models;

public enum PasswordVerificationResult
{
    Failed = 0,
    Success = 1,
    SuccessRehashNeeded = 2
}
