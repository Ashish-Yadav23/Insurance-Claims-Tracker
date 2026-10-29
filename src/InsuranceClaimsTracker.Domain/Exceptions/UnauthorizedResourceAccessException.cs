namespace InsuranceClaimsTracker.Domain.Exceptions;

public class UnauthorizedResourceAccessException : DomainException
{
    public UnauthorizedResourceAccessException(string message) : base(message)
    {
    }

    public UnauthorizedResourceAccessException(string resourceName, object resourceId)
        : base($"Access to {resourceName} with ID '{resourceId}' is unauthorized.")
    {
    }
}
