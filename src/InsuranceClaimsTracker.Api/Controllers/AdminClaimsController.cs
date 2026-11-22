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
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartReview(
        int id,
        [FromBody] StartReviewRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _adminClaimService.StartReviewAsync(id, request, cancellationToken);
        if (!result.IsSuccess)
        {
            return StatusCode(result.StatusCode, new { message = result.ErrorMessage });
        }

        return Ok(result.Value);
    }

    [HttpPost("{id:int}/decision")]
    [ProducesResponseType(typeof(ClaimResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Decision(
        int id,
        [FromBody] ClaimDecisionRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _adminClaimService.ProcessDecisionAsync(id, request, cancellationToken);
        if (!result.IsSuccess)
        {
            return StatusCode(result.StatusCode, new { message = result.ErrorMessage });
        }

        return Ok(result.Value);
    }

    [HttpPost("{id:int}/payout")]
    [ProducesResponseType(typeof(ClaimResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Payout(
        int id,
        [FromBody] ClaimPayoutRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _adminClaimService.RecordPayoutAsync(id, request, cancellationToken);
        if (!result.IsSuccess)
        {
            return StatusCode(result.StatusCode, new { message = result.ErrorMessage });
        }

        return Ok(result.Value);
    }
}
