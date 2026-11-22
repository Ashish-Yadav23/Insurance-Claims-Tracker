using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SecurityClaim = System.Security.Claims.Claim;

namespace InsuranceClaimsTracker.Infrastructure.Authentication;

public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtSettings _jwtSettings;

    public JwtTokenGenerator(IOptions<JwtSettings> jwtOptions)
    {
        _jwtSettings = jwtOptions.Value;

        if (string.IsNullOrWhiteSpace(_jwtSettings.SigningKey) || Encoding.UTF8.GetByteCount(_jwtSettings.SigningKey) < 32)
        {
            throw new InvalidOperationException("JWT SigningKey must be at least 256 bits (32 bytes).");
        }
    }

    public GeneratedToken GenerateToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);

        var claims = new List<SecurityClaim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("role", user.Role.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Name, user.Name)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = credentials
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        var expiresInSeconds = (int)(expiresAt - now).TotalSeconds;

        return new GeneratedToken(tokenString, "Bearer", expiresAt, expiresInSeconds);
    }
}
