using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using Xunit;

namespace InsuranceClaimsTracker.UnitTests.Domain;

public class UserTests
{
    [Fact]
    public void Constructor_WithValidParameters_InitializesUserCorrectly()
    {
        // Arrange & Act
        var user = new User("Jane Doe", "jane@example.com", "HashedSecret123");

        // Assert
        Assert.Equal("Jane Doe", user.Name);
        Assert.Equal("jane@example.com", user.Email);
        Assert.Equal("HashedSecret123", user.PasswordHash);
        Assert.Equal(UserRole.Customer, user.Role);
        Assert.True(user.CreatedAt <= DateTimeOffset.UtcNow);
        Assert.Null(user.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_WithInvalidName_ThrowsArgumentException(string? invalidName)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new User(invalidName!, "test@example.com", "Hashed123"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_WithInvalidEmail_ThrowsArgumentException(string? invalidEmail)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new User("Jane Doe", invalidEmail!, "Hashed123"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_WithInvalidPasswordHash_ThrowsArgumentException(string? invalidHash)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new User("Jane Doe", "test@example.com", invalidHash!));
    }
}
