namespace InsuranceClaimsTracker.Application.Common.Models;

public record GeneratedToken(
    string Token,
    string TokenType,
    DateTimeOffset ExpiresAt,
    int ExpiresInSeconds);
