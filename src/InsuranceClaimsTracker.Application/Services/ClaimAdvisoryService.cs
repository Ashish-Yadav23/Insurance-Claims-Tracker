using System.Text.Json;
using System.Text.RegularExpressions;
using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Application.DTOs.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InsuranceClaimsTracker.Application.Services;

public class ClaimAdvisoryService : IClaimAdvisoryService
{
    private readonly IApplicationDbContext _context;
    private readonly ILlmClient _llmClient;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ClaimAdvisoryService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ClaimAdvisoryService(
        IApplicationDbContext context,
        ILlmClient llmClient,
        ICurrentUserService currentUserService,
        ILogger<ClaimAdvisoryService> logger)
    {
        _context = context;
        _llmClient = llmClient;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ServiceResult<ClaimAdvisoryResponseDto>> GetClaimAdvisoryAsync(
        int claimId,
        CancellationToken cancellationToken = default)
    {
        // 1. Authorization checks: Admin role required
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return ServiceResult<ClaimAdvisoryResponseDto>.Failure("Unauthorized.", 401);
        }

        if (!_currentUserService.IsAdmin)
        {
            return ServiceResult<ClaimAdvisoryResponseDto>.Failure("Forbidden: Administrator access required.", 403);
        }

        // 2. Retrieve claim and policy context from database (never trust client input)
        var claim = await _context.Claims
            .Include(c => c.Policy)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == claimId, cancellationToken);

        if (claim == null)
        {
            return ServiceResult<ClaimAdvisoryResponseDto>.Failure("Claim not found.", 404);
        }

        if (string.IsNullOrWhiteSpace(claim.Description))
        {
            return ServiceResult<ClaimAdvisoryResponseDto>.Failure("Claim description cannot be empty.", 400);
        }

        // 3. Prepare data-minimized payload (no PII, names, emails, policy numbers, or credentials)
        var llmRequest = new LlmAdvisoryRequest
        {
            PolicyType = claim.Policy?.PolicyType.ToString() ?? "Unknown",
            ClaimAmount = claim.ClaimAmount,
            IncidentDate = claim.IncidentDate,
            Description = claim.Description.Trim()
        };

        // 4. Request advisory generation from LLM client
        var llmResult = await _llmClient.GenerateClaimAdvisoryAsync(llmRequest, cancellationToken);
        if (!llmResult.IsSuccess)
        {
            _logger.LogWarning("LLM provider returned failure for claim {ClaimId}: {ErrorMessage} (Status: {StatusCode})",
                claimId, llmResult.ErrorMessage, llmResult.StatusCode);

            return ServiceResult<ClaimAdvisoryResponseDto>.Failure(
                llmResult.ErrorMessage ?? "The AI advisory service is temporarily unavailable. Please try again later.",
                llmResult.StatusCode);
        }

        // 5. Independently validate and sanitize returned JSON
        var validationResult = ParseAndValidateResponse(llmResult.RawJson);
        if (!validationResult.IsSuccess)
        {
            _logger.LogWarning("AI provider output failed validation for claim {ClaimId}: {Error}",
                claimId, validationResult.ErrorMessage);

            return ServiceResult<ClaimAdvisoryResponseDto>.Failure(
                validationResult.ErrorMessage!,
                validationResult.StatusCode);
        }

        // Transient advisory response: Never alters claim state, payout, or ClaimHistory
        return ServiceResult<ClaimAdvisoryResponseDto>.Success(validationResult.Value!, 200);
    }

    private static ServiceResult<ClaimAdvisoryResponseDto> ParseAndValidateResponse(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return ServiceResult<ClaimAdvisoryResponseDto>.Failure(
                "The AI advisory service returned an empty response.", 503);
        }

        var cleanedJson = ExtractJsonContent(rawJson);

        RawLlmPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<RawLlmPayload>(cleanedJson, JsonOptions);
        }
        catch (JsonException)
        {
            return ServiceResult<ClaimAdvisoryResponseDto>.Failure(
                "The AI advisory service returned a malformed response format.", 503);
        }

        if (payload == null)
        {
            return ServiceResult<ClaimAdvisoryResponseDto>.Failure(
                "The AI advisory service returned an invalid response structure.", 503);
        }

        // Validate Category against controlled list
        if (!ClaimAdvisoryConstants.TryNormalizeCategory(payload.Category, out var normalizedCategory))
        {
            return ServiceResult<ClaimAdvisoryResponseDto>.Failure(
                $"The AI advisory service returned an unsupported category '{payload.Category}'.", 503);
        }

        // Validate Priority against controlled list
        var rawPriority = !string.IsNullOrWhiteSpace(payload.SuggestedPriority)
            ? payload.SuggestedPriority
            : payload.Priority;

        if (!ClaimAdvisoryConstants.TryNormalizePriority(rawPriority, out var normalizedPriority))
        {
            return ServiceResult<ClaimAdvisoryResponseDto>.Failure(
                $"The AI advisory service returned an unsupported priority '{rawPriority}'.", 503);
        }

        // Validate Summary
        if (string.IsNullOrWhiteSpace(payload.Summary))
        {
            return ServiceResult<ClaimAdvisoryResponseDto>.Failure(
                "The AI advisory service returned an empty summary.", 503);
        }

        var trimmedSummary = payload.Summary.Trim();
        if (trimmedSummary.Length > ClaimAdvisoryConstants.MaxSummaryLength)
        {
            return ServiceResult<ClaimAdvisoryResponseDto>.Failure(
                $"The AI advisory service returned an oversized summary ({trimmedSummary.Length} characters).", 503);
        }

        var dto = new ClaimAdvisoryResponseDto
        {
            Category = normalizedCategory,
            Summary = trimmedSummary,
            SuggestedPriority = normalizedPriority
        };

        return ServiceResult<ClaimAdvisoryResponseDto>.Success(dto, 200);
    }

    private static string ExtractJsonContent(string input)
    {
        var trimmed = input.Trim();

        // Handle ```json ... ``` code fence if returned
        var codeBlockMatch = Regex.Match(trimmed, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        if (codeBlockMatch.Success)
        {
            return codeBlockMatch.Groups[1].Value.Trim();
        }

        return trimmed;
    }

    private class RawLlmPayload
    {
        public string? Category { get; set; }
        public string? Summary { get; set; }
        public string? SuggestedPriority { get; set; }
        public string? Priority { get; set; }
    }
}
