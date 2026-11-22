namespace InsuranceClaimsTracker.Application.DTOs.Claims;

public class ClaimHistoryResponseDto
{
    public int Id { get; set; }
    public int ClaimId { get; set; }
    public string? PreviousStatus { get; set; }
    public string NewStatus { get; set; } = string.Empty;
    public int ChangedById { get; set; }
    public string ChangedByName { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; }
    public string? ReviewNote { get; set; }
}
