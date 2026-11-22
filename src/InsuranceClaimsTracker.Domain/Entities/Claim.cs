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
    public decimal? ApprovedAmount { get; set; }
    public string? PaymentReference { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
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

    public void StartReview(int adminId, string? reviewNote = null)
    {
        if (!CanTransitionTo(ClaimStatus.UnderReview))
        {
            throw new InvalidClaimStateException($"Cannot start review on a claim with status '{Status}'. Only claims in 'Submitted' status can enter review.");
        }

        var previousStatus = Status;
        Status = ClaimStatus.UnderReview;
        UpdatedAt = DateTimeOffset.UtcNow;

        var note = string.IsNullOrWhiteSpace(reviewNote) ? "Review initiated by administrator." : reviewNote.Trim();
        var history = new ClaimHistory(Id, previousStatus, ClaimStatus.UnderReview, adminId, note);
        History.Add(history);
    }

    public void Approve(int adminId, decimal approvedAmount, string reviewNote)
    {
        if (string.IsNullOrWhiteSpace(reviewNote))
        {
            throw new ArgumentException("A review note is required when approving a claim.", nameof(reviewNote));
        }

        if (approvedAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(approvedAmount), "Approved amount must be greater than zero.");
        }

        if (approvedAmount > ClaimAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(approvedAmount), $"Approved amount ({approvedAmount}) cannot exceed the claimed amount ({ClaimAmount}).");
        }

        if (!CanTransitionTo(ClaimStatus.Approved))
        {
            throw new InvalidClaimStateException($"Cannot approve a claim with status '{Status}'. Only claims in 'UnderReview' status can be approved.");
        }

        var previousStatus = Status;
        Status = ClaimStatus.Approved;
        ApprovedAmount = approvedAmount;
        UpdatedAt = DateTimeOffset.UtcNow;

        var history = new ClaimHistory(Id, previousStatus, ClaimStatus.Approved, adminId, reviewNote.Trim());
        History.Add(history);
    }

    public void Reject(int adminId, string reviewNote)
    {
        if (string.IsNullOrWhiteSpace(reviewNote))
        {
            throw new ArgumentException("A review note is required when rejecting a claim.", nameof(reviewNote));
        }

        if (!CanTransitionTo(ClaimStatus.Rejected))
        {
            throw new InvalidClaimStateException($"Cannot reject a claim with status '{Status}'. Only claims in 'UnderReview' status can be rejected.");
        }

        var previousStatus = Status;
        Status = ClaimStatus.Rejected;
        ApprovedAmount = null;
        UpdatedAt = DateTimeOffset.UtcNow;

        var history = new ClaimHistory(Id, previousStatus, ClaimStatus.Rejected, adminId, reviewNote.Trim());
        History.Add(history);
    }

    public void RecordPayout(int adminId, string paymentReference, DateTimeOffset? paidAt = null)
    {
        if (string.IsNullOrWhiteSpace(paymentReference))
        {
            throw new ArgumentException("A payment reference is required when recording a payout.", nameof(paymentReference));
        }

        if (Status == ClaimStatus.Paid)
        {
            throw new InvalidClaimStateException("Duplicate payout attempt: this claim has already been paid.");
        }

        if (!CanTransitionTo(ClaimStatus.Paid))
        {
            throw new InvalidClaimStateException($"Cannot record payout for a claim with status '{Status}'. Only claims in 'Approved' status can be marked as paid.");
        }

        var previousStatus = Status;
        Status = ClaimStatus.Paid;
        PaymentReference = paymentReference.Trim();
        PaidAt = paidAt ?? DateTimeOffset.UtcNow;
        UpdatedAt = PaidAt;

        var history = new ClaimHistory(Id, previousStatus, ClaimStatus.Paid, adminId, $"Payment reference: {PaymentReference}");
        History.Add(history);
    }
}
