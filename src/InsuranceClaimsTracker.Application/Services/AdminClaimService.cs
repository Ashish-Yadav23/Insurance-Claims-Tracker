using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Application.DTOs.Admin;
using InsuranceClaimsTracker.Application.DTOs.Claims;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace InsuranceClaimsTracker.Application.Services;

public class AdminClaimService : IAdminClaimService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public const string ConcurrencyConflictMessage = "The claim has been modified by another operation. Please reload and try again.";

    public AdminClaimService(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ServiceResult<ClaimResponseDto>> StartReviewAsync(
        int claimId,
        StartReviewRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var authCheck = ValidateAdminAccess();
        if (!authCheck.IsSuccess)
        {
            return ServiceResult<ClaimResponseDto>.Failure(authCheck.ErrorMessage!, authCheck.StatusCode);
        }
        var adminId = authCheck.Value;

        if (request == null)
        {
            return ServiceResult<ClaimResponseDto>.Failure("Request payload cannot be empty.", 400);
        }

        var (isValidToken, clientRowVersion, tokenError) = ParseRowVersion(request.RowVersion);
        if (!isValidToken)
        {
            return ServiceResult<ClaimResponseDto>.Failure(tokenError!, 400);
        }

        var claim = await LoadClaimWithDetailsAsync(claimId, cancellationToken);
        if (claim == null)
        {
            return ServiceResult<ClaimResponseDto>.Failure("Claim not found.", 404);
        }

        if (IsConcurrencyConflict(claim, clientRowVersion!))
        {
            return ServiceResult<ClaimResponseDto>.Failure(ConcurrencyConflictMessage, 409);
        }

        SetConcurrencyOriginalValue(claim, clientRowVersion!);

        try
        {
            claim.StartReview(adminId, request.ReviewNote);
        }
        catch (InvalidClaimStateException ex)
        {
            return ServiceResult<ClaimResponseDto>.Failure(ex.Message, 400);
        }

        return await SaveChangesAndMapResultAsync(claim, cancellationToken);
    }

    public async Task<ServiceResult<ClaimResponseDto>> ProcessDecisionAsync(
        int claimId,
        ClaimDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var authCheck = ValidateAdminAccess();
        if (!authCheck.IsSuccess)
        {
            return ServiceResult<ClaimResponseDto>.Failure(authCheck.ErrorMessage!, authCheck.StatusCode);
        }
        var adminId = authCheck.Value;

        if (request == null)
        {
            return ServiceResult<ClaimResponseDto>.Failure("Request payload cannot be empty.", 400);
        }

        var (isValidToken, clientRowVersion, tokenError) = ParseRowVersion(request.RowVersion);
        if (!isValidToken)
        {
            return ServiceResult<ClaimResponseDto>.Failure(tokenError!, 400);
        }

        if (string.IsNullOrWhiteSpace(request.Decision))
        {
            return ServiceResult<ClaimResponseDto>.Failure("Decision is required ('Approved' or 'Rejected').", 400);
        }

        if (string.IsNullOrWhiteSpace(request.ReviewNote))
        {
            return ServiceResult<ClaimResponseDto>.Failure("A non-empty review note is required when recording a decision.", 400);
        }

        var claim = await LoadClaimWithDetailsAsync(claimId, cancellationToken);
        if (claim == null)
        {
            return ServiceResult<ClaimResponseDto>.Failure("Claim not found.", 404);
        }

        if (IsConcurrencyConflict(claim, clientRowVersion!))
        {
            return ServiceResult<ClaimResponseDto>.Failure(ConcurrencyConflictMessage, 409);
        }

        SetConcurrencyOriginalValue(claim, clientRowVersion!);

        var decision = request.Decision.Trim();
        if (decision.Equals("Approved", StringComparison.OrdinalIgnoreCase))
        {
            if (!request.ApprovedAmount.HasValue)
            {
                return ServiceResult<ClaimResponseDto>.Failure("Approved amount is required when approving a claim.", 400);
            }

            try
            {
                claim.Approve(adminId, request.ApprovedAmount.Value, request.ReviewNote);
            }
            catch (Exception ex) when (ex is InvalidClaimStateException or ArgumentException or ArgumentOutOfRangeException)
            {
                return ServiceResult<ClaimResponseDto>.Failure(ex.Message, 400);
            }
        }
        else if (decision.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
        {
            if (request.ApprovedAmount.HasValue)
            {
                return ServiceResult<ClaimResponseDto>.Failure("Approved amount must not be provided for rejected claims.", 400);
            }

            try
            {
                claim.Reject(adminId, request.ReviewNote);
            }
            catch (Exception ex) when (ex is InvalidClaimStateException or ArgumentException)
            {
                return ServiceResult<ClaimResponseDto>.Failure(ex.Message, 400);
            }
        }
        else
        {
            return ServiceResult<ClaimResponseDto>.Failure("Decision must be either 'Approved' or 'Rejected'.", 400);
        }

        return await SaveChangesAndMapResultAsync(claim, cancellationToken);
    }

    public async Task<ServiceResult<ClaimResponseDto>> RecordPayoutAsync(
        int claimId,
        ClaimPayoutRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var authCheck = ValidateAdminAccess();
        if (!authCheck.IsSuccess)
        {
            return ServiceResult<ClaimResponseDto>.Failure(authCheck.ErrorMessage!, authCheck.StatusCode);
        }
        var adminId = authCheck.Value;

        if (request == null)
        {
            return ServiceResult<ClaimResponseDto>.Failure("Request payload cannot be empty.", 400);
        }

        var (isValidToken, clientRowVersion, tokenError) = ParseRowVersion(request.RowVersion);
        if (!isValidToken)
        {
            return ServiceResult<ClaimResponseDto>.Failure(tokenError!, 400);
        }

        if (string.IsNullOrWhiteSpace(request.PaymentReference))
        {
            return ServiceResult<ClaimResponseDto>.Failure("Payment reference is required.", 400);
        }

        var claim = await LoadClaimWithDetailsAsync(claimId, cancellationToken);
        if (claim == null)
        {
            return ServiceResult<ClaimResponseDto>.Failure("Claim not found.", 404);
        }

        if (IsConcurrencyConflict(claim, clientRowVersion!))
        {
            return ServiceResult<ClaimResponseDto>.Failure(ConcurrencyConflictMessage, 409);
        }

        SetConcurrencyOriginalValue(claim, clientRowVersion!);

        try
        {
            claim.RecordPayout(adminId, request.PaymentReference);
        }
        catch (Exception ex) when (ex is InvalidClaimStateException or ArgumentException)
        {
            return ServiceResult<ClaimResponseDto>.Failure(ex.Message, 400);
        }

        return await SaveChangesAndMapResultAsync(claim, cancellationToken);
    }

    private ServiceResult<int> ValidateAdminAccess()
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return ServiceResult<int>.Failure("Unauthorized.", 401);
        }

        if (!_currentUserService.IsAdmin)
        {
            return ServiceResult<int>.Failure("Forbidden: Administrator access required.", 403);
        }

        return ServiceResult<int>.Success(_currentUserService.UserId.Value);
    }

    private static (bool isValid, byte[]? rowVersion, string? error) ParseRowVersion(string rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            return (false, null, "RowVersion concurrency token is required.");
        }

        try
        {
            var bytes = Convert.FromBase64String(rowVersion.Trim());
            return (true, bytes, null);
        }
        catch (FormatException)
        {
            return (false, null, "Invalid RowVersion format. Must be a valid Base64 string.");
        }
    }

    private async Task<Claim?> LoadClaimWithDetailsAsync(int claimId, CancellationToken cancellationToken)
    {
        return await _context.Claims
            .Include(c => c.Policy)
                .ThenInclude(p => p!.Customer)
            .Include(c => c.History)
                .ThenInclude(h => h.ChangedBy)
            .FirstOrDefaultAsync(c => c.Id == claimId, cancellationToken);
    }

    private static bool IsConcurrencyConflict(Claim claim, byte[] clientRowVersion)
    {
        return claim.RowVersion != null && !claim.RowVersion.SequenceEqual(clientRowVersion);
    }

    private void SetConcurrencyOriginalValue(Claim claim, byte[] clientRowVersion)
    {
        try
        {
            _context.Entry(claim).Property(c => c.RowVersion).OriginalValue = clientRowVersion;
        }
        catch
        {
            // Fallback for providers that don't track RowVersion via DbContext.Entry
        }
    }

    private async Task<ServiceResult<ClaimResponseDto>> SaveChangesAndMapResultAsync(
        Claim claim,
        CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<ClaimResponseDto>.Failure(ConcurrencyConflictMessage, 409);
        }

        var response = MapToDto(claim);
        return ServiceResult<ClaimResponseDto>.Success(response, 200);
    }

    private static ClaimResponseDto MapToDto(Claim claim)
    {
        return new ClaimResponseDto
        {
            Id = claim.Id,
            PolicyId = claim.PolicyId,
            PolicyNumber = claim.Policy?.PolicyNumber ?? string.Empty,
            CustomerId = claim.Policy?.CustomerId ?? 0,
            CustomerName = claim.Policy?.Customer?.Name ?? string.Empty,
            ClaimAmount = claim.ClaimAmount,
            ApprovedAmount = claim.ApprovedAmount,
            PaymentReference = claim.PaymentReference,
            PaidAt = claim.PaidAt,
            IncidentDate = claim.IncidentDate,
            Description = claim.Description,
            Status = claim.Status.ToString(),
            RowVersion = claim.RowVersion != null ? Convert.ToBase64String(claim.RowVersion) : null,
            CreatedAt = claim.CreatedAt,
            UpdatedAt = claim.UpdatedAt,
            History = claim.History
                .OrderBy(h => h.ChangedAt)
                .Select(h => new ClaimHistoryResponseDto
                {
                    Id = h.Id,
                    ClaimId = h.ClaimId,
                    PreviousStatus = h.PreviousStatus?.ToString(),
                    NewStatus = h.NewStatus.ToString(),
                    ChangedById = h.ChangedById,
                    ChangedByName = h.ChangedBy?.Name ?? string.Empty,
                    ChangedAt = h.ChangedAt,
                    ReviewNote = h.ReviewNote
                })
                .ToList()
        };
    }
}
