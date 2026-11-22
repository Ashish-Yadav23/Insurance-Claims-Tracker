using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using InsuranceClaimsTracker.Application.DTOs.Auth;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace InsuranceClaimsTracker.IntegrationTests;

public class AuthEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AuthEndpointTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_WithValidCustomerPayload_ReturnsCreatedAndSafeProfile()
    {
        // Arrange
        var uniqueEmail = $"user_{Guid.NewGuid():N}@example.com";
        var request = new
        {
            name = "Test User",
            email = uniqueEmail,
            password = "SecurePassword123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var profile = JsonSerializer.Deserialize<UserProfileDto>(content, JsonOptions);

        Assert.NotNull(profile);
        Assert.True(profile.Id > 0);
        Assert.Equal("Test User", profile.Name);
        Assert.Equal(uniqueEmail.ToLowerInvariant(), profile.Email);
        Assert.Equal("Customer", profile.Role);

        // Verify password hash is NOT exposed
        Assert.DoesNotContain("passwordHash", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_WithRoleAttemptInPayload_IgnoresRoleAndEnforcesCustomer()
    {
        // Arrange
        var uniqueEmail = $"admin_attempt_{Guid.NewGuid():N}@example.com";
        var request = new
        {
            name = "Attacker Trying Admin",
            email = uniqueEmail,
            password = "SecurePassword123!",
            role = "Admin" // Attempt to elevate role
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var profile = JsonSerializer.Deserialize<UserProfileDto>(content, JsonOptions);

        Assert.NotNull(profile);
        Assert.Equal("Customer", profile.Role);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsConflict()
    {
        // Arrange
        var uniqueEmail = $"duplicate_{Guid.NewGuid():N}@example.com";
        var request = new
        {
            name = "First User",
            email = uniqueEmail,
            password = "SecurePassword123!"
        };

        var firstResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        // Act
        var secondResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Register_WithShortPassword_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            name = "Short Pwd User",
            email = $"shortpwd_{Guid.NewGuid():N}@example.com",
            password = "Short1!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOkAndJwtToken()
    {
        // Arrange
        var uniqueEmail = $"login_success_{Guid.NewGuid():N}@example.com";
        var password = "ValidPassword123!";

        var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            name = "Login Success User",
            email = uniqueEmail,
            password = password
        });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var loginRequest = new
        {
            email = uniqueEmail,
            password = password
        };

        // Act
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var content = await loginResponse.Content.ReadAsStringAsync();
        var authResult = JsonSerializer.Deserialize<AuthResponseDto>(content, JsonOptions);

        Assert.NotNull(authResult);
        Assert.NotEmpty(authResult.Token);
        Assert.Equal("Bearer", authResult.TokenType);
        Assert.True(authResult.ExpiresIn > 0);
        Assert.NotNull(authResult.User);
        Assert.Equal("Login Success User", authResult.User.Name);
        Assert.Equal(uniqueEmail.ToLowerInvariant(), authResult.User.Email);
        Assert.Equal("Customer", authResult.User.Role);
        Assert.DoesNotContain("passwordHash", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorizedWithGenericError()
    {
        // Arrange
        var uniqueEmail = $"bad_pwd_{Guid.NewGuid():N}@example.com";
        var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            name = "Bad Pwd User",
            email = uniqueEmail,
            password = "CorrectPassword123!"
        });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var loginRequest = new
        {
            email = uniqueEmail,
            password = "WrongPassword123!"
        };

        // Act
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);

        var content = await loginResponse.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(content);
        var message = jsonDoc.RootElement.GetProperty("message").GetString();
        Assert.Equal("Invalid email or password.", message);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsUnauthorizedWithGenericError()
    {
        // Arrange
        var loginRequest = new
        {
            email = $"nonexistent_{Guid.NewGuid():N}@example.com",
            password = "SomePassword123!"
        };

        // Act
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);

        var content = await loginResponse.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(content);
        var message = jsonDoc.RootElement.GetProperty("message").GetString();
        Assert.Equal("Invalid email or password.", message);
    }

    [Fact]
    public async Task GetCurrentUser_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_WithValidBearerToken_ReturnsOkAndUserProfile()
    {
        // Arrange: Register and login to obtain token
        var uniqueEmail = $"me_endpoint_{Guid.NewGuid():N}@example.com";
        var password = "ValidPassword123!";
        var userName = "Me Endpoint User";

        var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            name = userName,
            email = uniqueEmail,
            password = password
        });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = uniqueEmail,
            password = password
        });
        var authContent = await loginResponse.Content.ReadAsStringAsync();
        var authResult = JsonSerializer.Deserialize<AuthResponseDto>(authContent, JsonOptions);
        Assert.NotNull(authResult);

        using var authorizedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        authorizedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authResult.Token);

        // Act
        var response = await _client.SendAsync(authorizedRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var profile = JsonSerializer.Deserialize<UserProfileDto>(content, JsonOptions);

        Assert.NotNull(profile);
        Assert.Equal(userName, profile.Name);
        Assert.Equal(uniqueEmail.ToLowerInvariant(), profile.Email);
        Assert.Equal("Customer", profile.Role);
        Assert.DoesNotContain("passwordHash", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCurrentUser_WithInvalidToken_ReturnsUnauthorized()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.jwt.token");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_WithExpiredToken_ReturnsUnauthorized()
    {
        // Arrange
        var expiredToken = CreateTestToken(expires: DateTime.UtcNow.AddMinutes(-5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_WithForgedSignatureKeyToken_ReturnsUnauthorized()
    {
        // Arrange
        var rogueKey = "RogueUntrustedKeyThatDoesNotMatchTheConfiguredKey1234567890!";
        var forgedToken = CreateTestToken(signingKey: rogueKey);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_WithWrongIssuerToken_ReturnsUnauthorized()
    {
        // Arrange
        var wrongIssuerToken = CreateTestToken(issuer: "RogueUntrustedIssuer");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", wrongIssuerToken);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_WithWrongAudienceToken_ReturnsUnauthorized()
    {
        // Arrange
        var wrongAudienceToken = CreateTestToken(audience: "RogueUntrustedAudience");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", wrongAudienceToken);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string CreateTestToken(
        string? signingKey = null,
        string? issuer = null,
        string? audience = null,
        DateTime? expires = null,
        int userId = 1,
        string role = "Customer")
    {
        var key = signingKey ?? "SuperSecretTestingSigningKeyForJwtValidation1234567890!";
        var iss = issuer ?? "InsuranceClaimsTracker-IntegrationTests";
        var aud = audience ?? "InsuranceClaimsTracker-IntegrationApp";
        var exp = expires ?? DateTime.UtcNow.AddMinutes(20);
        var notBefore = exp < DateTime.UtcNow ? exp.AddMinutes(-30) : DateTime.UtcNow;

        var tokenHandler = new JwtSecurityTokenHandler();
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<System.Security.Claims.Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role),
            new("role", role)
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
