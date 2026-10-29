using InsuranceClaimsTracker.Domain.Enums;
using InsuranceClaimsTracker.Domain.Exceptions;

namespace InsuranceClaimsTracker.Domain.Entities;

public class Claim
{
    public int Id { get; set; }
    public int PolicyId { get; set; }
    public Policy? Policy { get; set; }
    public decimal ClaimAmount { get; set; }
    public DateTimeOffset IncidentDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public ClaimStatus Status { get; set; } = ClaimStatus.Submitted;
    public string? AiSuggestedCategory { get; set; }
    public string? AiSummary { get; set; }
    public byte[]? RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation property for EF Core (Phase 2)
    public ICollection<ClaimHistory> History { get; set; } = new List<ClaimHistory>();

    public Claim() { }

    public Claim(
        int policyId,
        decimal claimAmount,
        DateTimeOffset incidentDate,
        string description)
    {
        if (claimAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(claimAmount), "Claim amount must be greater than zero.");

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Claim description cannot be empty.", nameof(description));

        PolicyId = policyId;
        ClaimAmount = claimAmount;
        IncidentDate = incidentDate;
        Description = description.Trim();
        Status = ClaimStatus.Submitted;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public bool CanTransitionTo(ClaimStatus targetStatus)
    {
        return (Status, targetStatus) switch
        {
            (ClaimStatus.Submitted, ClaimStatus.UnderReview) => true,
            (ClaimStatus.UnderReview, ClaimStatus.Approved) => true,
            (ClaimStatus.UnderReview, ClaimStatus.Rejected) => true,
            (ClaimStatus.Approved, ClaimStatus.Paid) => true,
            _ => false
        };
    }
}
