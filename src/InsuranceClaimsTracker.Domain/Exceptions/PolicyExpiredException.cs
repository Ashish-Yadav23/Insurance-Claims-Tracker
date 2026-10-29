namespace InsuranceClaimsTracker.Domain.Exceptions;

public class PolicyExpiredException : DomainException
{
    public PolicyExpiredException(string message) : base(message)
    {
    }
}
