using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using InsuranceClaimsTracker.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace InsuranceClaimsTracker.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            _logger.LogWarning("The response has already started, cannot write ProblemDetails for exception: {Message}", exception.Message);
            return;
        }

        var (statusCode, title, detail, isClientError) = MapException(exception);

        if (isClientError)
        {
            _logger.LogWarning(exception, "Client error ({StatusCode} {Title}) occurred during {Method} {Path}: {Detail}",
                statusCode, title, context.Request.Method, context.Request.Path, detail);
        }
        else
        {
            _logger.LogError(exception, "Unhandled unexpected error occurred during {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        problemDetails.Extensions["traceId"] = traceId;
        problemDetails.Extensions["message"] = detail;

        await context.Response.WriteAsJsonAsync(problemDetails, JsonOptions);
    }

    public static (int StatusCode, string Title, string Detail, bool IsClientError) MapException(Exception exception)
    {
        return exception switch
        {
            // Unauthorized resource access (BOLA / IDOR defense) - 404 to avoid disclosing existence of other users' resources
            UnauthorizedResourceAccessException ex =>
                ((int)HttpStatusCode.NotFound, "Not Found", ex.Message, true),

            // Key not found -> 404
            KeyNotFoundException ex =>
                ((int)HttpStatusCode.NotFound, "Not Found", ex.Message, true),

            // Invalid claim state transitions -> 400
            InvalidClaimStateException ex =>
                ((int)HttpStatusCode.BadRequest, "Bad Request", ex.Message, true),

            // Policy expired -> 400
            PolicyExpiredException ex =>
                ((int)HttpStatusCode.BadRequest, "Bad Request", ex.Message, true),

            // Coverage exceeded -> 400
            CoverageExceededException ex =>
                ((int)HttpStatusCode.BadRequest, "Bad Request", ex.Message, true),

            // Other domain exceptions -> 400
            DomainException ex =>
                ((int)HttpStatusCode.BadRequest, "Bad Request", ex.Message, true),

            // Validation exceptions -> 400
            ValidationException ex =>
                ((int)HttpStatusCode.BadRequest, "Bad Request", ex.Message, true),

            // Argument exceptions -> 400
            ArgumentException ex =>
                ((int)HttpStatusCode.BadRequest, "Bad Request", ex.Message, true),

            // Bad HTTP request -> 400
            BadHttpRequestException ex =>
                ((int)HttpStatusCode.BadRequest, "Bad Request", ex.Message, true),

            // Unauthorized access exceptions -> 401
            UnauthorizedAccessException ex =>
                ((int)HttpStatusCode.Unauthorized, "Unauthorized", ex.Message, true),

            // Optimistic concurrency conflict -> 409
            DbUpdateConcurrencyException =>
                ((int)HttpStatusCode.Conflict, "Conflict", "The claim has been modified by another operation. Please reload and try again.", true),

            // Database update exception -> Check for known unique constraint violations
            DbUpdateException dbEx when TryMapUniqueConstraintViolation(dbEx, out var constraintMessage) =>
                ((int)HttpStatusCode.Conflict, "Conflict", constraintMessage, true),

            // Unhandled unexpected exceptions -> 500 (Never expose stack traces, DB strings, or internal details)
            _ =>
                ((int)HttpStatusCode.InternalServerError, "Internal Server Error", "An unexpected error occurred while processing your request. Please try again later.", false)
        };
    }

    private static bool TryMapUniqueConstraintViolation(DbUpdateException exception, out string message)
    {
        // Check for SQL Server unique constraint / unique index violation (error codes 2601 and 2627)
        var sqlEx = exception.InnerException as SqlException ?? exception.GetBaseException() as SqlException;
        if (sqlEx != null && (sqlEx.Number == 2601 || sqlEx.Number == 2627))
        {
            var sqlMsg = sqlEx.Message;
            if (sqlMsg.Contains("IX_Users_Email", StringComparison.OrdinalIgnoreCase) ||
                (sqlMsg.Contains("Users", StringComparison.OrdinalIgnoreCase) && sqlMsg.Contains("Email", StringComparison.OrdinalIgnoreCase)))
            {
                message = "A user with this email address already exists.";
                return true;
            }

            if (sqlMsg.Contains("IX_Policies_PolicyNumber", StringComparison.OrdinalIgnoreCase) ||
                (sqlMsg.Contains("Policies", StringComparison.OrdinalIgnoreCase) && sqlMsg.Contains("PolicyNumber", StringComparison.OrdinalIgnoreCase)))
            {
                message = "A policy with this policy number already exists.";
                return true;
            }

            message = "A record with the specified unique values already exists.";
            return true;
        }

        // Generic fallback for in-memory or SQLite providers if testing throws DbUpdateException with unique message
        var baseMessage = exception.GetBaseException().Message;
        if (baseMessage.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
            baseMessage.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
        {
            if (baseMessage.Contains("Email", StringComparison.OrdinalIgnoreCase))
            {
                message = "A user with this email address already exists.";
                return true;
            }

            if (baseMessage.Contains("PolicyNumber", StringComparison.OrdinalIgnoreCase))
            {
                message = "A policy with this policy number already exists.";
                return true;
            }

            message = "A record with the specified unique values already exists.";
            return true;
        }

        message = string.Empty;
        return false;
    }
}
