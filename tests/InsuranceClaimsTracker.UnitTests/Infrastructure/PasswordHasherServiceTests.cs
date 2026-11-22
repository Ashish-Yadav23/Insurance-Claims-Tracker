using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Infrastructure.Authentication;
using Xunit;

namespace InsuranceClaimsTracker.UnitTests.Infrastructure;

public class PasswordHasherServiceTests
{
    private readonly PasswordHasherService _hasher = new();

    [Fact]
    public void HashPassword_ReturnsHashedStringDifferentFromPlaintext()
    {
        const string password = "SuperSecurePassword123!";

        var hash = _hasher.HashPassword(password);

        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
        Assert.NotEqual(password, hash);
    }

    [Fact]
    public void HashPassword_GeneratesUniqueSaltsForSamePassword()
    {
        const string password = "SuperSecurePassword123!";

        var hash1 = _hasher.HashPassword(password);
        var hash2 = _hasher.HashPassword(password);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void VerifyHashedPassword_WithCorrectPassword_ReturnsSuccess()
    {
        const string password = "SuperSecurePassword123!";
        var hash = _hasher.HashPassword(password);

        var result = _hasher.VerifyHashedPassword(hash, password);

        Assert.True(result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded);
    }

    [Fact]
    public void VerifyHashedPassword_WithIncorrectPassword_ReturnsFailed()
    {
        const string password = "SuperSecurePassword123!";
        var hash = _hasher.HashPassword(password);

        var result = _hasher.VerifyHashedPassword(hash, "WrongPassword456!");

        Assert.Equal(PasswordVerificationResult.Failed, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void HashPassword_WithEmptyOrNullPassword_ThrowsArgumentException(string? invalidPassword)
    {
        Assert.Throws<ArgumentException>(() => _hasher.HashPassword(invalidPassword!));
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("hash", "")]
    [InlineData(null, "password")]
    [InlineData("hash", null)]
    public void VerifyHashedPassword_WithEmptyOrNull_ReturnsFailed(string? hash, string? password)
    {
        var result = _hasher.VerifyHashedPassword(hash!, password!);
        Assert.Equal(PasswordVerificationResult.Failed, result);
    }
}
