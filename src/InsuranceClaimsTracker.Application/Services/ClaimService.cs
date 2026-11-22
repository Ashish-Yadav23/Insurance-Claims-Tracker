using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Application.DTOs.Claims;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InsuranceClaimsTracker.Application.Services;

public class ClaimService : IClaimService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public const int MaxDescriptionLength = 2000;

    public ClaimService(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ServiceResult<ClaimResponseDto>> SubmitClaimAsync(
        CreateClaimRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return ServiceResult<ClaimResponseDto>.Failure("Unauthorized.", 401);
        }

        if (!_currentUserService.IsCustomer)
        {
            return ServiceResult<ClaimResponseDto>.Failure("Only customers can submit claims.", 403);
        }

        if (request == null)
        {
            return ServiceResult<ClaimResponseDto>.Failure("Request payload cannot be empty.", 400);
        }

        if (request.ClaimAmount <= 0)
        {
            return ServiceResult<ClaimResponseDto>.Failure("Claim amount must be greater than zero.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return ServiceResult<ClaimResponseDto>.Failure("Description is required.", 400);
        }

        var trimmedDescription = request.Description.Trim();
        if (trimmedDescription.Length > MaxDescriptionLength)
        {
            return ServiceResult<ClaimResponseDto>.Failure($"Description cannot exceed {MaxDescriptionLength} characters.", 400);
        }

        var currentCustomerId = _currentUserService.UserId.Value;

        var policy = await _context.Policies
            .Include(p => p.Customer)
            .FirstOrDefaultAsync(p => p.Id == request.PolicyId, cancellationToken);

        // Security check: verify policy exists and belongs to authenticated customer
        if (policy == null || policy.CustomerId != currentCustomerId)
        {
            // Return 404 to avoid disclosing existence of other customers' policies
            return ServiceResult<ClaimResponseDto>.Failure("Policy not found.", 404);
        }

        if (!policy.IsActive)
        {
            return ServiceResult<ClaimResponseDto>.Failure("Cannot file a claim against an inactive policy.", 400);
        }

        var now = DateTimeOffset.UtcNow;
        if (request.IncidentDate > now)
        {
            return ServiceResult<ClaimResponseDto>.Failure("Incident date cannot be in the future.", 400);
        }

        if (request.IncidentDate < policy.StartDate || request.IncidentDate > policy.EndDate)
        {
            return ServiceResult<ClaimResponseDto>.Failure(
                $"Incident date ({request.IncidentDate:yyyy-MM-dd}) must fall within policy coverage dates ({policy.StartDate:yyyy-MM-dd} to {policy.EndDate:yyyy-MM-dd}).", 400);
        }

        if (request.ClaimAmount > policy.CoverageAmount)
        {
            return ServiceResult<ClaimResponseDto>.Failure(
                $"Claim amount ({request.ClaimAmount}) cannot exceed policy coverage amount ({policy.CoverageAmount}).", 400);
        }

        // Server sets initial status strictly to Submitted and timestamps to UTC
        var claim = new Claim(
            policy.Id,
            request.ClaimAmount,
            request.IncidentDate,
            trimmedDescription);

        // Audit history: Initial record created consistently
        var history = new ClaimHistory(
            claimId: 0,
            previousStatus: null,
            newStatus: ClaimStatus.Submitted,
            changedById: currentCustomerId,
            reviewNote: null);

        claim.History.Add(history);
        _context.Claims.Add(claim);

        await _context.SaveChangesAsync(cancellationToken);

        var customerName = policy.Customer?.Name ?? string.Empty;

        var response = new ClaimResponseDto
        {
            Id = claim.Id,
            PolicyId = policy.Id,
            PolicyNumber = policy.PolicyNumber,
            CustomerId = policy.CustomerId,
            CustomerName = customerName,
            ClaimAmount = claim.ClaimAmount,
            IncidentDate = claim.IncidentDate,
            Description = claim.Description,
            Status = claim.Status.ToString(),
            CreatedAt = claim.CreatedAt,
            UpdatedAt = claim.UpdatedAt,
            History = new List<ClaimHistoryResponseDto>
            {
                new()
                {
                    Id = history.Id,
                    ClaimId = claim.Id,
                    PreviousStatus = null,
                    NewStatus = ClaimStatus.Submitted.ToString(),
                    ChangedById = currentCustomerId,
                    ChangedByName = customerName,
                    ChangedAt = history.ChangedAt,
                    ReviewNote = null
                }
            }
        };

        return ServiceResult<ClaimResponseDto>.Success(response, 201);
    }

    public async Task<ServiceResult<IEnumerable<ClaimResponseDto>>> GetClaimsAsync(
        int? policyId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return ServiceResult<IEnumerable<ClaimResponseDto>>.Failure("Unauthorized.", 401);
        }

        var query = _context.Claims
            .Include(c => c.Policy)
                .ThenInclude(p => p!.Customer)
            .Include(c => c.History)
                .ThenInclude(h => h.ChangedBy)
            .AsNoTracking();

        if (_currentUserService.IsCustomer)
        {
            var currentCustomerId = _currentUserService.UserId.Value;
            query = query.Where(c => c.Policy != null && c.Policy.CustomerId == currentCustomerId);

            if (policyId.HasValue)
            {
                query = query.Where(c => c.PolicyId == policyId.Value);
            }
        }
        else if (_currentUserService.IsAdmin)
        {
            if (policyId.HasValue)
            {
                query = query.Where(c => c.PolicyId == policyId.Value);
            }
        }
        else
        {
            return ServiceResult<IEnumerable<ClaimResponseDto>>.Failure("Forbidden.", 403);
        }

        var claims = await query
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ClaimResponseDto
            {
                Id = c.Id,
                PolicyId = c.PolicyId,
                PolicyNumber = c.Policy != null ? c.Policy.PolicyNumber : string.Empty,
                CustomerId = c.Policy != null ? c.Policy.CustomerId : 0,
                CustomerName = c.Policy != null && c.Policy.Customer != null ? c.Policy.Customer.Name : string.Empty,
                ClaimAmount = c.ClaimAmount,
                IncidentDate = c.IncidentDate,
                Description = c.Description,
                Status = c.Status.ToString(),
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                History = c.History
                    .OrderBy(h => h.ChangedAt)
                    .Select(h => new ClaimHistoryResponseDto
                    {
                        Id = h.Id,
                        ClaimId = h.ClaimId,
                        PreviousStatus = h.PreviousStatus != null ? h.PreviousStatus.ToString() : null,
                        NewStatus = h.NewStatus.ToString(),
                        ChangedById = h.ChangedById,
                        ChangedByName = h.ChangedBy != null ? h.ChangedBy.Name : string.Empty,
                        ChangedAt = h.ChangedAt,
                        ReviewNote = h.ReviewNote
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return ServiceResult<IEnumerable<ClaimResponseDto>>.Success(claims);
    }

    public async Task<ServiceResult<ClaimResponseDto>> GetClaimByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return ServiceResult<ClaimResponseDto>.Failure("Unauthorized.", 401);
        }

        var claim = await _context.Claims
            .Include(c => c.Policy)
                .ThenInclude(p => p!.Customer)
            .Include(c => c.History)
                .ThenInclude(h => h.ChangedBy)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (claim == null)
        {
            return ServiceResult<ClaimResponseDto>.Failure("Claim not found.", 404);
        }

        if (_currentUserService.IsCustomer)
        {
            // Resource ownership check: return 404 if claim does not belong to customer's policy
            if (claim.Policy == null || claim.Policy.CustomerId != _currentUserService.UserId.Value)
            {
                return ServiceResult<ClaimResponseDto>.Failure("Claim not found.", 404);
            }
        }
        else if (!_currentUserService.IsAdmin)
        {
            return ServiceResult<ClaimResponseDto>.Failure("Forbidden.", 403);
        }

        var response = new ClaimResponseDto
        {
            Id = claim.Id,
            PolicyId = claim.PolicyId,
            PolicyNumber = claim.Policy != null ? claim.Policy.PolicyNumber : string.Empty,
            CustomerId = claim.Policy != null ? claim.Policy.CustomerId : 0,
            CustomerName = claim.Policy != null && claim.Policy.Customer != null ? claim.Policy.Customer.Name : string.Empty,
            ClaimAmount = claim.ClaimAmount,
            IncidentDate = claim.IncidentDate,
            Description = claim.Description,
            Status = claim.Status.ToString(),
            CreatedAt = claim.CreatedAt,
            UpdatedAt = claim.UpdatedAt,
            History = claim.History
                .OrderBy(h => h.ChangedAt)
                .Select(h => new ClaimHistoryResponseDto
                {
                    Id = h.Id,
                    ClaimId = h.ClaimId,
                    PreviousStatus = h.PreviousStatus != null ? h.PreviousStatus.ToString() : null,
                    NewStatus = h.NewStatus.ToString(),
                    ChangedById = h.ChangedById,
                    ChangedByName = h.ChangedBy != null ? h.ChangedBy.Name : string.Empty,
                    ChangedAt = h.ChangedAt,
                    ReviewNote = h.ReviewNote
                })
                .ToList()
        };

        return ServiceResult<ClaimResponseDto>.Success(response);
    }
}
