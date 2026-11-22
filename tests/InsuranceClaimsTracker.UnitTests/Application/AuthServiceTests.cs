using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Application.DTOs.Auth;
using InsuranceClaimsTracker.Application.Services;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using InsuranceClaimsTracker.Infrastructure.Authentication;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace InsuranceClaimsTracker.UnitTests.Application;

public class AuthServiceTests
{
    private readonly ApplicationDbContext _dbContext;
    private readonly PasswordHasherService _passwordHasher;
    private readonly JwtTokenGenerator _tokenGenerator;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(dbOptions);
        _passwordHasher = new PasswordHasherService();

        var jwtSettings = Options.Create(new JwtSettings
        {
            Issuer = "InsuranceClaimsTracker-Test",
            Audience = "InsuranceClaimsTracker-TestApp",
            SigningKey = "SuperSecretKeyForTestingPurposesMustBeLongEnough123456!",
            AccessTokenExpirationMinutes = 20
        });

        _tokenGenerator = new JwtTokenGenerator(jwtSettings);
        _authService = new AuthService(_dbContext, _passwordHasher, _tokenGenerator);
    }

    [Fact]
    public async Task RegisterAsync_WithValidCustomerDetails_CreatesCustomerUserAndReturnsCreated()
    {
        // Arrange
        var request = new RegisterRequestDto
        {
            Name = "Alice Smith",
            Email = "alice.smith@example.com",
            Password = "SecurePassword123!"
        };

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(201, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal("Alice Smith", result.Value.Name);
        Assert.Equal("alice.smith@example.com", result.Value.Email);
        Assert.Equal("Customer", result.Value.Role);
        Assert.True(result.Value.Id > 0);

        var savedUser = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == result.Value.Id);
        Assert.NotNull(savedUser);
        Assert.Equal(UserRole.Customer, savedUser.Role);
        Assert.NotEqual("SecurePassword123!", savedUser.PasswordHash);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateEmail_ReturnsConflict()
    {
        // Arrange
        var existingUser = new User("Existing User", "existing@example.com", _passwordHasher.HashPassword("Password1234!"), UserRole.Customer);
        _dbContext.Users.Add(existingUser);
        await _dbContext.SaveChangesAsync();

        var request = new RegisterRequestDto
        {
            Name = "New Person",
            Email = "existing@example.com",
            Password = "AnotherPassword123!"
        };

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("A user with this email address already exists.", result.ErrorMessage);
    }

    [Fact]
    public async Task RegisterAsync_WithMixedCaseEmail_NormalizesToLowercase()
    {
        // Arrange
        var request = new RegisterRequestDto
        {
            Name = "Bob Doe",
            Email = "Bob.Doe@EXAMPLE.Com",
            Password = "ValidPassword123!"
        };

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("bob.doe@example.com", result.Value?.Email);

        var savedUser = await _dbContext.Users.SingleOrDefaultAsync(u => u.Email == "bob.doe@example.com");
        Assert.NotNull(savedUser);
    }

    [Theory]
    [InlineData("Short1!")]
    [InlineData("12345678901")]
    public async Task RegisterAsync_WithPasswordShorterThan12Chars_ReturnsBadRequest(string shortPassword)
    {
        // Arrange
        var request = new RegisterRequestDto
        {
            Name = "Test User",
            Email = "test@example.com",
            Password = shortPassword
        };

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("at least 12 characters", result.ErrorMessage);
    }

    [Fact]
    public async Task RegisterAsync_WithPasswordLongerThan128Chars_ReturnsBadRequest()
    {
        // Arrange
        var longPassword = new string('A', 129);
        var request = new RegisterRequestDto
        {
            Name = "Test User",
            Email = "test@example.com",
            Password = longPassword
        };

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("cannot exceed 128 characters", result.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RegisterAsync_WithEmptyOrWhitespaceName_ReturnsBadRequest(string name)
    {
        // Arrange
        var request = new RegisterRequestDto
        {
            Name = name,
            Email = "test@example.com",
            Password = "ValidPassword123!"
        };

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("Name is required.", result.ErrorMessage);
    }

    [Fact]
    public async Task RegisterAsync_WithNameExceeding100Chars_ReturnsBadRequest()
    {
        // Arrange
        var longName = new string('N', 101);
        var request = new RegisterRequestDto
        {
            Name = longName,
            Email = "test@example.com",
            Password = "ValidPassword123!"
        };

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("Name cannot exceed 100 characters", result.ErrorMessage);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("no-domain@")]
    [InlineData("@missing-user.com")]
    public async Task RegisterAsync_WithInvalidEmailFormat_ReturnsBadRequest(string invalidEmail)
    {
        // Arrange
        var request = new RegisterRequestDto
        {
            Name = "Valid Name",
            Email = invalidEmail,
            Password = "ValidPassword123!"
        };

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("Invalid email address format.", result.ErrorMessage);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsSuccessWithAuthResponseAndProfile()
    {
        // Arrange
        var password = "CorrectPassword123!";
        var user = new User("John Doe", "john.doe@example.com", _passwordHasher.HashPassword(password), UserRole.Customer);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var request = new LoginRequestDto
        {
            Email = "JOHN.DOE@example.com", // Case insensitive login
            Password = password
        };

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value.Token);
        Assert.Equal("Bearer", result.Value.TokenType);
        Assert.True(result.Value.ExpiresIn > 0);
        Assert.Equal("John Doe", result.Value.User.Name);
        Assert.Equal("john.doe@example.com", result.Value.User.Email);
        Assert.Equal("Customer", result.Value.User.Role);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsUnauthorizedWithGenericError()
    {
        // Arrange
        var user = new User("John Doe", "john.doe@example.com", _passwordHasher.HashPassword("CorrectPassword123!"), UserRole.Customer);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var request = new LoginRequestDto
        {
            Email = "john.doe@example.com",
            Password = "WrongPassword123!"
        };

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
        Assert.Equal("Invalid email or password.", result.ErrorMessage);
    }

    [Fact]
    public async Task LoginAsync_WithNonExistentEmail_ReturnsUnauthorizedWithGenericError()
    {
        // Arrange
        var request = new LoginRequestDto
        {
            Email = "nonexistent@example.com",
            Password = "SomePassword123!"
        };

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
        Assert.Equal("Invalid email or password.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetCurrentUserProfileAsync_WithExistingUserId_ReturnsProfileWithoutPasswordHash()
    {
        // Arrange
        var user = new User("Admin User", "admin@example.com", _passwordHasher.HashPassword("AdminPassword123!"), UserRole.Admin);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        var profile = await _authService.GetCurrentUserProfileAsync(user.Id);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(user.Id, profile.Id);
        Assert.Equal("Admin User", profile.Name);
        Assert.Equal("admin@example.com", profile.Email);
        Assert.Equal("Admin", profile.Role);
    }

    [Fact]
    public async Task GetCurrentUserProfileAsync_WithUnknownUserId_ReturnsNull()
    {
        // Act
        var profile = await _authService.GetCurrentUserProfileAsync(9999);

        // Assert
        Assert.Null(profile);
    }
}
