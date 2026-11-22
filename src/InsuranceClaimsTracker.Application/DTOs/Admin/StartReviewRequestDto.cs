namespace InsuranceClaimsTracker.Application.DTOs.Admin;

public class StartReviewRequestDto
{
    public string? ReviewNote { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
