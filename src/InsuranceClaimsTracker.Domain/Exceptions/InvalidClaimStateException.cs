namespace InsuranceClaimsTracker.Domain.Exceptions;

public class InvalidClaimStateException : DomainException
{
    public InvalidClaimStateException(string message) : base(message)
    {
    }
}
