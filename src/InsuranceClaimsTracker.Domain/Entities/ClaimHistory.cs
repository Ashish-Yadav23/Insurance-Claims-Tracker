using InsuranceClaimsTracker.Domain.Enums;

namespace InsuranceClaimsTracker.Domain.Entities;

public class ClaimHistory
{
    public int Id { get; set; }
    public int ClaimId { get; set; }
    public Claim? Claim { get; set; }
    public ClaimStatus? PreviousStatus { get; set; }
    public ClaimStatus NewStatus { get; set; }
    public int ChangedById { get; set; }
    public User? ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ReviewNote { get; set; }

    public ClaimHistory() { }

    public ClaimHistory(
        int claimId,
        ClaimStatus? previousStatus,
        ClaimStatus newStatus,
        int changedById,
        string? reviewNote = null)
    {
        ClaimId = claimId;
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
        ChangedById = changedById;
        ReviewNote = string.IsNullOrWhiteSpace(reviewNote) ? null : reviewNote.Trim();
        ChangedAt = DateTimeOffset.UtcNow;
    }
}
