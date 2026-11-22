using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Application.DTOs.Policies;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InsuranceClaimsTracker.Application.Services;

public class PolicyService : IPolicyService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public const int MaxPolicyNumberLength = 50;

    public PolicyService(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ServiceResult<PolicyResponseDto>> CreatePolicyAsync(
        CreatePolicyRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUserService.IsAuthenticated)
        {
            return ServiceResult<PolicyResponseDto>.Failure("Unauthorized.", 401);
        }

        if (!_currentUserService.IsAdmin)
        {
            return ServiceResult<PolicyResponseDto>.Failure("Only administrators can create policies.", 403);
        }

        if (request == null)
        {
            return ServiceResult<PolicyResponseDto>.Failure("Request payload cannot be empty.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.PolicyNumber))
        {
            return ServiceResult<PolicyResponseDto>.Failure("Policy number is required.", 400);
        }

        var normalizedPolicyNumber = request.PolicyNumber.Trim().ToUpperInvariant();
        if (normalizedPolicyNumber.Length > MaxPolicyNumberLength)
        {
            return ServiceResult<PolicyResponseDto>.Failure($"Policy number cannot exceed {MaxPolicyNumberLength} characters.", 400);
        }

        if (!Enum.IsDefined(typeof(PolicyType), request.PolicyType))
        {
            return ServiceResult<PolicyResponseDto>.Failure("Invalid policy type.", 400);
        }

        if (request.CoverageAmount <= 0)
        {
            return ServiceResult<PolicyResponseDto>.Failure("Coverage amount must be greater than zero.", 400);
        }

        if (request.EndDate <= request.StartDate)
        {
            return ServiceResult<PolicyResponseDto>.Failure("Policy end date must be after start date.", 400);
        }

        var customer = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == request.CustomerId, cancellationToken);

        if (customer == null)
        {
            return ServiceResult<PolicyResponseDto>.Failure($"Customer with ID {request.CustomerId} does not exist.", 400);
        }

        if (customer.Role != UserRole.Customer)
        {
            return ServiceResult<PolicyResponseDto>.Failure("Policies can only be assigned to accounts with the Customer role.", 400);
        }

        var policyNumberExists = await _context.Policies
            .AnyAsync(p => p.PolicyNumber == normalizedPolicyNumber, cancellationToken);

        if (policyNumberExists)
        {
            return ServiceResult<PolicyResponseDto>.Failure($"A policy with number '{normalizedPolicyNumber}' already exists.", 409);
        }

        var policy = new Policy(
            normalizedPolicyNumber,
            request.CustomerId,
            request.PolicyType,
            request.CoverageAmount,
            request.StartDate,
            request.EndDate);

        try
        {
            _context.Policies.Add(policy);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return ServiceResult<PolicyResponseDto>.Failure($"A policy with number '{normalizedPolicyNumber}' already exists.", 409);
        }

        var response = new PolicyResponseDto
        {
            Id = policy.Id,
            PolicyNumber = policy.PolicyNumber,
            CustomerId = policy.CustomerId,
            CustomerName = customer.Name,
            CustomerEmail = customer.Email,
            PolicyType = policy.PolicyType.ToString(),
            CoverageAmount = policy.CoverageAmount,
            StartDate = policy.StartDate,
            EndDate = policy.EndDate,
            IsActive = policy.IsActive,
            CreatedAt = policy.CreatedAt
        };

        return ServiceResult<PolicyResponseDto>.Success(response, 201);
    }

    public async Task<ServiceResult<IEnumerable<PolicyResponseDto>>> GetPoliciesAsync(
        int? customerId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return ServiceResult<IEnumerable<PolicyResponseDto>>.Failure("Unauthorized.", 401);
        }

        var query = _context.Policies
            .Include(p => p.Customer)
            .AsNoTracking();

        if (_currentUserService.IsCustomer)
        {
            var currentUserId = _currentUserService.UserId.Value;
            query = query.Where(p => p.CustomerId == currentUserId);
        }
        else if (_currentUserService.IsAdmin)
        {
            if (customerId.HasValue)
            {
                query = query.Where(p => p.CustomerId == customerId.Value);
            }
        }
        else
        {
            return ServiceResult<IEnumerable<PolicyResponseDto>>.Failure("Forbidden.", 403);
        }

        var policies = await query
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PolicyResponseDto
            {
                Id = p.Id,
                PolicyNumber = p.PolicyNumber,
                CustomerId = p.CustomerId,
                CustomerName = p.Customer != null ? p.Customer.Name : string.Empty,
                CustomerEmail = p.Customer != null ? p.Customer.Email : string.Empty,
                PolicyType = p.PolicyType.ToString(),
                CoverageAmount = p.CoverageAmount,
                StartDate = p.StartDate,
                EndDate = p.EndDate,
                IsActive = p.IsActive,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return ServiceResult<IEnumerable<PolicyResponseDto>>.Success(policies);
    }

    public async Task<ServiceResult<PolicyResponseDto>> GetPolicyByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return ServiceResult<PolicyResponseDto>.Failure("Unauthorized.", 401);
        }

        var policy = await _context.Policies
            .Include(p => p.Customer)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (policy == null)
        {
            return ServiceResult<PolicyResponseDto>.Failure("Policy not found.", 404);
        }

        if (_currentUserService.IsCustomer)
        {
            // Resource ownership check: return 404 if customer does not own policy
            if (policy.CustomerId != _currentUserService.UserId.Value)
            {
                return ServiceResult<PolicyResponseDto>.Failure("Policy not found.", 404);
            }
        }
        else if (!_currentUserService.IsAdmin)
        {
            return ServiceResult<PolicyResponseDto>.Failure("Forbidden.", 403);
        }

        var response = new PolicyResponseDto
        {
            Id = policy.Id,
            PolicyNumber = policy.PolicyNumber,
            CustomerId = policy.CustomerId,
            CustomerName = policy.Customer != null ? policy.Customer.Name : string.Empty,
            CustomerEmail = policy.Customer != null ? policy.Customer.Email : string.Empty,
            PolicyType = policy.PolicyType.ToString(),
            CoverageAmount = policy.CoverageAmount,
            StartDate = policy.StartDate,
            EndDate = policy.EndDate,
            IsActive = policy.IsActive,
            CreatedAt = policy.CreatedAt
        };

        return ServiceResult<PolicyResponseDto>.Success(response);
    }
}
