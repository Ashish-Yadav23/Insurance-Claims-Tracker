namespace InsuranceClaimsTracker.Application.DTOs.Admin;

public class ClaimPayoutRequestDto
{
    public string PaymentReference { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}
