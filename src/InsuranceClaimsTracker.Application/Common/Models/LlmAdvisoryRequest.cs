namespace InsuranceClaimsTracker.Application.Common.Models;

public class LlmAdvisoryRequest
{
    public string PolicyType { get; set; } = string.Empty;
    public decimal ClaimAmount { get; set; }
    public DateTimeOffset IncidentDate { get; set; }
    public string Description { get; set; } = string.Empty;
}
