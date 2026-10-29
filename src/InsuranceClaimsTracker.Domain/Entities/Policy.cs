using InsuranceClaimsTracker.Domain.Enums;
using InsuranceClaimsTracker.Domain.Exceptions;

namespace InsuranceClaimsTracker.Domain.Entities;

public class Policy
{
    public int Id { get; set; }
    public string PolicyNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public User? Customer { get; set; }
    public PolicyType PolicyType { get; set; }
    public decimal CoverageAmount { get; set; }
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties for EF Core (Phase 2)
    public ICollection<Claim> Claims { get; set; } = new List<Claim>();

    public Policy() { }

    public Policy(
        string policyNumber,
        int customerId,
        PolicyType policyType,
        decimal coverageAmount,
        DateTimeOffset startDate,
        DateTimeOffset endDate)
    {
        if (string.IsNullOrWhiteSpace(policyNumber))
            throw new ArgumentException("Policy number cannot be empty.", nameof(policyNumber));

        if (coverageAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(coverageAmount), "Coverage amount must be greater than zero.");

        if (endDate <= startDate)
            throw new ArgumentException("Policy end date must be after start date.", nameof(endDate));

        PolicyNumber = policyNumber.Trim().ToUpperInvariant();
        CustomerId = customerId;
        PolicyType = policyType;
        CoverageAmount = coverageAmount;
        StartDate = startDate;
        EndDate = endDate;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsActiveOn(DateTimeOffset date)
    {
        return IsActive && date >= StartDate && date <= EndDate;
    }
}
