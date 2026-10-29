namespace InsuranceClaimsTracker.Domain.Exceptions;

public class CoverageExceededException : DomainException
{
    public CoverageExceededException(string message) : base(message)
    {
    }

    public CoverageExceededException(decimal claimAmount, decimal coverageAmount)
        : base($"Claim amount ({claimAmount:C}) exceeds policy coverage limit ({coverageAmount:C}).")
    {
    }
}
