namespace InsuranceClaimsTracker.Application.DTOs.Admin;

public class ClaimDecisionRequestDto
{
    public string Decision { get; set; } = string.Empty;
    public decimal? ApprovedAmount { get; set; }
    public string ReviewNote { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}
