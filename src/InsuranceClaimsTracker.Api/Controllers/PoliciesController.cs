using InsuranceClaimsTracker.Api.Extensions;
using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Application.DTOs.Policies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InsuranceClaimsTracker.Api.Controllers;

[ApiController]
[Route("api/v1/policies")]
[Authorize]
public class PoliciesController : ControllerBase
{
    private readonly IPolicyService _policyService;

    public PoliciesController(IPolicyService policyService)
    {
        _policyService = policyService;
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(PolicyResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePolicy(
        [FromBody] CreatePolicyRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _policyService.CreatePolicyAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result);
        }

        return CreatedAtAction(
            nameof(GetPolicyById),
            new { id = result.Value!.Id },
            result.Value);
    }

    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IEnumerable<PolicyResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPolicies(
        [FromQuery] int? customerId,
        CancellationToken cancellationToken)
    {
        var result = await _policyService.GetPoliciesAsync(customerId, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result);
        }

        return Ok(result.Value);
    }

    [HttpGet("{id:int}")]
    [Authorize]
    [ProducesResponseType(typeof(PolicyResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPolicyById(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await _policyService.GetPolicyByIdAsync(id, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result);
        }

        return Ok(result.Value);
    }
}
