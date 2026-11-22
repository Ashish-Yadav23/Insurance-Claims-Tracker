namespace InsuranceClaimsTracker.Application.DTOs.Claims;

public class ClaimResponseDto
{
    public int Id { get; set; }
    public int PolicyId { get; set; }
    public string PolicyNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal ClaimAmount { get; set; }
    public DateTimeOffset IncidentDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public IReadOnlyList<ClaimHistoryResponseDto> History { get; set; } = Array.Empty<ClaimHistoryResponseDto>();
}
