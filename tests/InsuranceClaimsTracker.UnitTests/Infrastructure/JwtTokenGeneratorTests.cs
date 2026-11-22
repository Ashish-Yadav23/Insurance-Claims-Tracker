using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using InsuranceClaimsTracker.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using SecurityClaim = System.Security.Claims.Claim;

namespace InsuranceClaimsTracker.UnitTests.Infrastructure;

public class JwtTokenGeneratorTests
{
    private readonly JwtSettings _validSettings = new()
    {
        Issuer = "InsuranceClaimsTrackerApi",
        Audience = "InsuranceClaimsTrackerClient",
        SigningKey = "A_Very_Strong_Cryptographic_Key_Of_At_Least_32_Bytes_Long!",
        AccessTokenExpirationMinutes = 20
    };

    [Fact]
    public void Constructor_WithKeyShorterThan32Bytes_ThrowsInvalidOperationException()
    {
        var shortKeySettings = new JwtSettings
        {
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            SigningKey = "ShortKeyUnder32Bytes!",
            AccessTokenExpirationMinutes = 20
        };

        Assert.Throws<InvalidOperationException>(() =>
            new JwtTokenGenerator(Options.Create(shortKeySettings)));
    }

    [Fact]
    public void GenerateToken_ReturnsValidJwtWithCorrectClaimsAndExpiration()
    {
        var generator = new JwtTokenGenerator(Options.Create(_validSettings));
        var user = new User("Jane Doe", "jane@example.com", "HashedSecret123", UserRole.Customer)
        {
            Id = 42
        };

        var beforeGeneration = DateTimeOffset.UtcNow;
        var tokenResult = generator.GenerateToken(user);
        var afterGeneration = DateTimeOffset.UtcNow;

        Assert.NotNull(tokenResult);
        Assert.NotEmpty(tokenResult.Token);
        Assert.Equal("Bearer", tokenResult.TokenType);
        Assert.True(tokenResult.ExpiresInSeconds > 0);
        Assert.True(tokenResult.ExpiresAt > beforeGeneration.AddMinutes(19));
        Assert.True(tokenResult.ExpiresAt <= afterGeneration.AddMinutes(21));

        // Validate token signature and claims
        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_validSettings.SigningKey)),
            ValidateIssuer = true,
            ValidIssuer = _validSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = _validSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        var principal = tokenHandler.ValidateToken(tokenResult.Token, validationParameters, out var validatedToken);
        Assert.NotNull(validatedToken);

        var subClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var emailClaim = principal.FindFirst(ClaimTypes.Email)?.Value;
        var roleClaim = principal.FindFirst(ClaimTypes.Role)?.Value;

        Assert.Equal("42", subClaim);
        Assert.Equal("jane@example.com", emailClaim);
        Assert.Equal("Customer", roleClaim);
        Assert.True(principal.IsInRole("Customer"));
    }

    [Fact]
    public void GenerateToken_IncludesAdminRoleWhenUserIsAdmin()
    {
        var generator = new JwtTokenGenerator(Options.Create(_validSettings));
        var admin = new User("Admin User", "admin@example.com", "HashedSecret123", UserRole.Admin)
        {
            Id = 1
        };

        var tokenResult = generator.GenerateToken(admin);

        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_validSettings.SigningKey)),
            ValidateIssuer = true,
            ValidIssuer = _validSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = _validSettings.Audience,
            ValidateLifetime = false,
            RoleClaimType = ClaimTypes.Role
        };

        var principal = tokenHandler.ValidateToken(tokenResult.Token, validationParameters, out _);
        var roleClaim = principal.FindFirst(ClaimTypes.Role)?.Value;

        Assert.Equal("Admin", roleClaim);
        Assert.True(principal.IsInRole("Admin"));
    }

    [Fact]
    public void ValidateToken_WithExpiredToken_ThrowsSecurityTokenExpiredException()
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_validSettings.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new SecurityClaim(ClaimTypes.NameIdentifier, "1") }),
            IssuedAt = DateTime.UtcNow.AddMinutes(-30),
            NotBefore = DateTime.UtcNow.AddMinutes(-30),
            Expires = DateTime.UtcNow.AddMinutes(-10),
            Issuer = _validSettings.Issuer,
            Audience = _validSettings.Audience,
            SigningCredentials = credentials
        };

        var expiredToken = tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = true,
            ValidIssuer = _validSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = _validSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        Assert.Throws<SecurityTokenExpiredException>(() =>
            tokenHandler.ValidateToken(expiredToken, validationParameters, out _));
    }

    [Fact]
    public void ValidateToken_WithWrongSignatureKey_ThrowsSecurityTokenSignatureKeyNotFoundException()
    {
        var generator = new JwtTokenGenerator(Options.Create(_validSettings));
        var user = new User("Test", "test@example.com", "hash", UserRole.Customer) { Id = 1 };
        var tokenResult = generator.GenerateToken(user);

        var differentKey = "DifferentSecretKeyThatDoesNotMatchOriginalOneAtAll!";
        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(differentKey)),
            ValidateIssuer = true,
            ValidIssuer = _validSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = _validSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() =>
            tokenHandler.ValidateToken(tokenResult.Token, validationParameters, out _));
    }

    [Fact]
    public void ValidateToken_WithMismatchedIssuer_ThrowsSecurityTokenInvalidIssuerException()
    {
        var generator = new JwtTokenGenerator(Options.Create(_validSettings));
        var user = new User("Test", "test@example.com", "hash", UserRole.Customer) { Id = 1 };
        var tokenResult = generator.GenerateToken(user);

        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_validSettings.SigningKey)),
            ValidateIssuer = true,
            ValidIssuer = "ExpectedDifferentIssuer",
            ValidateAudience = true,
            ValidAudience = _validSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        Assert.Throws<SecurityTokenInvalidIssuerException>(() =>
            tokenHandler.ValidateToken(tokenResult.Token, validationParameters, out _));
    }

    [Fact]
    public void ValidateToken_WithMismatchedAudience_ThrowsSecurityTokenInvalidAudienceException()
    {
        var generator = new JwtTokenGenerator(Options.Create(_validSettings));
        var user = new User("Test", "test@example.com", "hash", UserRole.Customer) { Id = 1 };
        var tokenResult = generator.GenerateToken(user);

        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_validSettings.SigningKey)),
            ValidateIssuer = true,
            ValidIssuer = _validSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = "ExpectedDifferentAudience",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        Assert.Throws<SecurityTokenInvalidAudienceException>(() =>
            tokenHandler.ValidateToken(tokenResult.Token, validationParameters, out _));
    }
}
