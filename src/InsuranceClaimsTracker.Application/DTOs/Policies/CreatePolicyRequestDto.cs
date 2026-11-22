using InsuranceClaimsTracker.Domain.Enums;

namespace InsuranceClaimsTracker.Application.DTOs.Policies;

public class CreatePolicyRequestDto
{
    public string PolicyNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public PolicyType PolicyType { get; set; }
    public decimal CoverageAmount { get; set; }
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
}
