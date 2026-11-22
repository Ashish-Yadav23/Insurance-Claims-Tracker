using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace InsuranceClaimsTracker.IntegrationTests;

public static class TestTokenHelper
{
    public const string DefaultSigningKey = "SuperSecretTestingSigningKeyForJwtValidation1234567890!";
    public const string DefaultIssuer = "InsuranceClaimsTracker-IntegrationTests";
    public const string DefaultAudience = "InsuranceClaimsTracker-IntegrationApp";

    public static string CreateToken(
        int userId,
        string role = "Customer",
        string email = "test@example.com",
        string name = "Test User",
        DateTime? expires = null,
        string? signingKey = null,
        string? issuer = null,
        string? audience = null)
    {
        var key = signingKey ?? DefaultSigningKey;
        var iss = issuer ?? DefaultIssuer;
        var aud = audience ?? DefaultAudience;
        var exp = expires ?? DateTime.UtcNow.AddMinutes(20);
        var notBefore = exp < DateTime.UtcNow ? exp.AddMinutes(-30) : DateTime.UtcNow;

        var tokenHandler = new JwtSecurityTokenHandler();
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Email, email),
            new(JwtRegisteredClaimNames.Name, name),
            new(ClaimTypes.Role, role),
            new("role", role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            IssuedAt = notBefore,
            NotBefore = notBefore,
            Expires = exp,
            Issuer = iss,
            Audience = aud,
            SigningCredentials = credentials
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
