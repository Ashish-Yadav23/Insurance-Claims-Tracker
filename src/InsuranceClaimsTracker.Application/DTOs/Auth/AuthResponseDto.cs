namespace InsuranceClaimsTracker.Application.DTOs.Auth;

public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public DateTimeOffset ExpiresAt { get; set; }
    public int ExpiresIn { get; set; }
    public UserProfileDto User { get; set; } = null!;
}
