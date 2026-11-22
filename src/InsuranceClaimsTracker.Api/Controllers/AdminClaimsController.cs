using InsuranceClaimsTracker.Api.Extensions;
using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Application.DTOs.Admin;
using InsuranceClaimsTracker.Application.DTOs.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InsuranceClaimsTracker.Api.Controllers;

[ApiController]
[Route("api/v1/admin/claims")]
[Authorize(Roles = "Admin")]
public class AdminClaimsController : ControllerBase
{
    private readonly IAdminClaimService _adminClaimService;

    public AdminClaimsController(IAdminClaimService adminClaimService)
    {
        _adminClaimService = adminClaimService;
    }

    [HttpPost("{id:int}/start-review")]
    [ProducesResponseType(typeof(ClaimResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartReview(
        int id,
        [FromBody] StartReviewRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _adminClaimService.StartReviewAsync(id, request, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result);
        }

        return Ok(result.Value);
    }

    [HttpPost("{id:int}/decision")]
    [ProducesResponseType(typeof(ClaimResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Decision(
        int id,
        [FromBody] ClaimDecisionRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _adminClaimService.ProcessDecisionAsync(id, request, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result);
        }

        return Ok(result.Value);
    }

    [HttpPost("{id:int}/payout")]
    [ProducesResponseType(typeof(ClaimResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Payout(
        int id,
        [FromBody] ClaimPayoutRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _adminClaimService.RecordPayoutAsync(id, request, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result);
        }

        return Ok(result.Value);
    }

    [HttpPost("{id:int}/advisory")]
    [ProducesResponseType(typeof(ClaimAdvisoryResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetAdvisory(
        int id,
        [FromServices] IClaimAdvisoryService advisoryService,
        CancellationToken cancellationToken)
    {
        var result = await advisoryService.GetClaimAdvisoryAsync(id, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result);
        }

        return Ok(result.Value);
    }
}
