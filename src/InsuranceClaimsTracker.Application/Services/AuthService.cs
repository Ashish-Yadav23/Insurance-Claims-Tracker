using System.ComponentModel.DataAnnotations;
using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Application.DTOs.Auth;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InsuranceClaimsTracker.Application.Services;

public class AuthService : IAuthService
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasherService _passwordHasher;
    private readonly IJwtTokenGenerator _tokenGenerator;

    public const int MinPasswordLength = 12;
    public const int MaxPasswordLength = 128;
    public const int MaxNameLength = 100;
    public const int MaxEmailLength = 256;

    public AuthService(
        IApplicationDbContext context,
        IPasswordHasherService passwordHasher,
        IJwtTokenGenerator tokenGenerator)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
    }

    public async Task<AuthResult<UserProfileDto>> RegisterAsync(
        RegisterRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return AuthResult<UserProfileDto>.Failure("Request payload cannot be empty.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AuthResult<UserProfileDto>.Failure("Name is required.", 400);
        }

        var trimmedName = request.Name.Trim();
        if (trimmedName.Length > MaxNameLength)
        {
            return AuthResult<UserProfileDto>.Failure($"Name cannot exceed {MaxNameLength} characters.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return AuthResult<UserProfileDto>.Failure("Email is required.", 400);
        }

        var trimmedEmail = request.Email.Trim();
        if (trimmedEmail.Length > MaxEmailLength)
        {
            return AuthResult<UserProfileDto>.Failure($"Email cannot exceed {MaxEmailLength} characters.", 400);
        }

        if (!new EmailAddressAttribute().IsValid(trimmedEmail) || !trimmedEmail.Contains('.'))
        {
            return AuthResult<UserProfileDto>.Failure("Invalid email address format.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < MinPasswordLength)
        {
            return AuthResult<UserProfileDto>.Failure($"Password must be at least {MinPasswordLength} characters long.", 400);
        }

        if (request.Password.Length > MaxPasswordLength)
        {
            return AuthResult<UserProfileDto>.Failure($"Password cannot exceed {MaxPasswordLength} characters.", 400);
        }

        var normalizedEmail = trimmedEmail.ToLowerInvariant();

        var emailExists = await _context.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (emailExists)
        {
            return AuthResult<UserProfileDto>.Failure("A user with this email address already exists.", 409);
        }

        var passwordHash = _passwordHasher.HashPassword(request.Password);

        // Security rule: Server unconditionally assigns Customer role. Public registration cannot create Admin.
        var user = new User(trimmedName, normalizedEmail, passwordHash, UserRole.Customer);

        try
        {
            _context.Users.Add(user);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Handles concurrent duplicate registrations gracefully
            return AuthResult<UserProfileDto>.Failure("A user with this email address already exists.", 409);
        }

        var profile = new UserProfileDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role.ToString(),
            CreatedAt = user.CreatedAt
        };

        return AuthResult<UserProfileDto>.Success(profile, 201);
    }

    public async Task<AuthResult<AuthResponseDto>> LoginAsync(
        LoginRequestDto request,
        CancellationToken cancellationToken = default)
    {
        const string genericAuthError = "Invalid email or password.";

        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return AuthResult<AuthResponseDto>.Failure(genericAuthError, 401);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (user == null)
        {
            // Do not reveal whether email is registered
            return AuthResult<AuthResponseDto>.Failure(genericAuthError, 401);
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(user.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return AuthResult<AuthResponseDto>.Failure(genericAuthError, 401);
        }

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(request.Password);
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        var token = _tokenGenerator.GenerateToken(user);

        var response = new AuthResponseDto
        {
            Token = token.Token,
            TokenType = token.TokenType,
            ExpiresAt = token.ExpiresAt,
            ExpiresIn = token.ExpiresInSeconds,
            User = new UserProfileDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role.ToString(),
                CreatedAt = user.CreatedAt
            }
        };

        return AuthResult<AuthResponseDto>.Success(response, 200);
    }

    public async Task<UserProfileDto?> GetCurrentUserProfileAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            return null;
        }

        return new UserProfileDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role.ToString(),
            CreatedAt = user.CreatedAt
        };
    }
}
