namespace InsuranceClaimsTracker.Application.DTOs.Admin;

public class ClaimAdvisoryResponseDto
{
    public string Category { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string SuggestedPriority { get; set; } = string.Empty;
}
