namespace InsuranceClaimsTracker.Application.DTOs.Claims;

public class CreateClaimRequestDto
{
    public int PolicyId { get; set; }
    public decimal ClaimAmount { get; set; }
    public DateTimeOffset IncidentDate { get; set; }
    public string Description { get; set; } = string.Empty;
}
