using System.Diagnostics;
using InsuranceClaimsTracker.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace InsuranceClaimsTracker.Api.Extensions;

public static class ApiResultExtensions
{
    public static IActionResult ToProblemDetails<T>(this ControllerBase controller, ServiceResult<T> result)
    {
        return controller.ToProblemDetails(result.StatusCode, result.ErrorMessage);
    }

    public static IActionResult ToProblemDetails<T>(this ControllerBase controller, AuthResult<T> result)
    {
        return controller.ToProblemDetails(result.StatusCode, result.ErrorMessage);
    }

    public static IActionResult ToProblemDetails(this ControllerBase controller, int statusCode, string? errorMessage)
    {
        var traceId = Activity.Current?.Id ?? controller.HttpContext.TraceIdentifier;
        var title = GetTitleForStatusCode(statusCode);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = errorMessage,
            Instance = controller.HttpContext.Request.Path
        };

        problemDetails.Extensions["traceId"] = traceId;
        problemDetails.Extensions["message"] = errorMessage;

        return controller.StatusCode(statusCode, problemDetails);
    }

    private static string GetTitleForStatusCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status500InternalServerError => "Internal Server Error",
        _ => "An error occurred"
    };
}
