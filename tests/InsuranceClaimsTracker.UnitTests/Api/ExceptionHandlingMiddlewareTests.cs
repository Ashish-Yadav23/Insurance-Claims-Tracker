using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using InsuranceClaimsTracker.Api.Middleware;
using InsuranceClaimsTracker.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InsuranceClaimsTracker.UnitTests.Api;

public class ExceptionHandlingMiddlewareTests
{
    private readonly NullLogger<ExceptionHandlingMiddleware> _logger = NullLogger<ExceptionHandlingMiddleware>.Instance;

    [Fact]
    public void MapException_CoverageExceededException_Returns400BadRequest()
    {
        var ex = new CoverageExceededException(5000m, 3000m);
        var (statusCode, title, detail, isClientError) = ExceptionHandlingMiddleware.MapException(ex);

        Assert.Equal(400, statusCode);
        Assert.Equal("Bad Request", title);
        Assert.Contains("exceeds policy coverage limit", detail);
        Assert.True(isClientError);
    }

    [Fact]
    public void MapException_PolicyExpiredException_Returns400BadRequest()
    {
        var ex = new PolicyExpiredException("Policy has expired.");
        var (statusCode, title, detail, isClientError) = ExceptionHandlingMiddleware.MapException(ex);

        Assert.Equal(400, statusCode);
        Assert.Equal("Bad Request", title);
        Assert.Equal("Policy has expired.", detail);
        Assert.True(isClientError);
    }

    [Fact]
    public void MapException_InvalidClaimStateException_Returns400BadRequest()
    {
        var ex = new InvalidClaimStateException("Cannot approve a claim that is in Submitted status.");
        var (statusCode, title, detail, isClientError) = ExceptionHandlingMiddleware.MapException(ex);

        Assert.Equal(400, statusCode);
        Assert.Equal("Bad Request", title);
        Assert.Equal("Cannot approve a claim that is in Submitted status.", detail);
        Assert.True(isClientError);
    }

    [Fact]
    public void MapException_UnauthorizedResourceAccessException_Returns404NotFoundForBolaDefense()
    {
        var ex = new UnauthorizedResourceAccessException("Policy", 42);
        var (statusCode, title, detail, isClientError) = ExceptionHandlingMiddleware.MapException(ex);

        Assert.Equal(404, statusCode);
        Assert.Equal("Not Found", title);
        Assert.Contains("42", detail);
        Assert.True(isClientError);
    }

    [Fact]
    public void MapException_KeyNotFoundException_Returns404NotFound()
    {
        var ex = new KeyNotFoundException("Resource with key '99' not found.");
        var (statusCode, title, detail, isClientError) = ExceptionHandlingMiddleware.MapException(ex);

        Assert.Equal(404, statusCode);
        Assert.Equal("Not Found", title);
        Assert.Equal("Resource with key '99' not found.", detail);
        Assert.True(isClientError);
    }

    [Fact]
    public void MapException_DbUpdateConcurrencyException_Returns409Conflict()
    {
        var ex = new DbUpdateConcurrencyException("Concurrency conflict occurred.");
        var (statusCode, title, detail, isClientError) = ExceptionHandlingMiddleware.MapException(ex);

        Assert.Equal(409, statusCode);
        Assert.Equal("Conflict", title);
        Assert.Contains("modified by another operation", detail);
        Assert.True(isClientError);
    }

    [Fact]
    public void MapException_DbUpdateException_WithEmailUniqueViolation_Returns409Conflict()
    {
        var inner = new Exception("Violation of UNIQUE KEY constraint 'IX_Users_Email'. Cannot insert duplicate key in object 'dbo.Users'.");
        var ex = new DbUpdateException("An error occurred while saving the entity changes.", inner);
        var (statusCode, title, detail, isClientError) = ExceptionHandlingMiddleware.MapException(ex);

        Assert.Equal(409, statusCode);
        Assert.Equal("Conflict", title);
        Assert.Equal("A user with this email address already exists.", detail);
        Assert.True(isClientError);
    }

    [Fact]
    public void MapException_DbUpdateException_WithPolicyNumberUniqueViolation_Returns409Conflict()
    {
        var inner = new Exception("Cannot insert duplicate key row in object 'dbo.Policies' with unique index 'IX_Policies_PolicyNumber'.");
        var ex = new DbUpdateException("An error occurred while saving the entity changes.", inner);
        var (statusCode, title, detail, isClientError) = ExceptionHandlingMiddleware.MapException(ex);

        Assert.Equal(409, statusCode);
        Assert.Equal("Conflict", title);
        Assert.Equal("A policy with this policy number already exists.", detail);
        Assert.True(isClientError);
    }

    [Fact]
    public void MapException_DbUpdateException_WithForeignKeyViolation_Returns500InternalServerError()
    {
        // Non-unique DB errors must NOT be mapped to 409
        var inner = new Exception("The INSERT statement conflicted with the FOREIGN KEY constraint 'FK_Claims_Policies_PolicyId'.");
        var ex = new DbUpdateException("Database update error", inner);
        var (statusCode, title, detail, isClientError) = ExceptionHandlingMiddleware.MapException(ex);

        Assert.Equal(500, statusCode);
        Assert.Equal("Internal Server Error", title);
        Assert.Equal("An unexpected error occurred while processing your request. Please try again later.", detail);
        Assert.False(isClientError);
    }

    [Fact]
    public void MapException_UnexpectedException_Returns500WithSafeMessage()
    {
        var ex = new InvalidOperationException("Sensitive internal database connection string: Server=secret;pwd=secret123;");
        var (statusCode, title, detail, isClientError) = ExceptionHandlingMiddleware.MapException(ex);

        Assert.Equal(500, statusCode);
        Assert.Equal("Internal Server Error", title);
        Assert.DoesNotContain("secret", detail);
        Assert.Equal("An unexpected error occurred while processing your request. Please try again later.", detail);
        Assert.False(isClientError);
    }

    [Fact]
    public void MapException_ValidationAndArgumentExceptions_Return400BadRequest()
    {
        var valEx = new ValidationException("Field is required.");
        var (vStatus, _, vDetail, vClient) = ExceptionHandlingMiddleware.MapException(valEx);
        Assert.Equal(400, vStatus);
        Assert.Equal("Field is required.", vDetail);
        Assert.True(vClient);

        var argEx = new ArgumentException("Invalid argument provided.");
        var (aStatus, _, aDetail, aClient) = ExceptionHandlingMiddleware.MapException(argEx);
        Assert.Equal(400, aStatus);
        Assert.Equal("Invalid argument provided.", aDetail);
        Assert.True(aClient);
    }

    [Fact]
    public async Task InvokeAsync_WhenExceptionThrown_WritesProblemDetailsJson()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/claims/test-exception";
        context.Request.Method = "POST";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = (ctx) => throw new InvalidClaimStateException("Invalid status transition.");
        var middleware = new ExceptionHandlingMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(400, context.Response.StatusCode);
        Assert.NotNull(context.Response.ContentType);
        Assert.StartsWith("application/json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var jsonDoc = await JsonDocument.ParseAsync(context.Response.Body);
        var root = jsonDoc.RootElement;

        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("Bad Request", root.GetProperty("title").GetString());
        Assert.Equal("Invalid status transition.", root.GetProperty("detail").GetString());
        Assert.Equal("/api/v1/claims/test-exception", root.GetProperty("instance").GetString());
        Assert.Equal("Invalid status transition.", root.GetProperty("message").GetString());
        Assert.True(root.TryGetProperty("traceId", out var traceIdProp));
        Assert.False(string.IsNullOrWhiteSpace(traceIdProp.GetString()));
    }

    [Fact]
    public async Task InvokeAsync_WhenUnexpectedExceptionThrown_DoesNotExposeStackTraceOrSensitiveDetails()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/claims/crash";
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = (ctx) => throw new Exception("Database password leaked: P@ssw0rd! at line 42 in SecretService.cs");
        var middleware = new ExceptionHandlingMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(500, context.Response.StatusCode);
        Assert.NotNull(context.Response.ContentType);
        Assert.StartsWith("application/json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Assert.DoesNotContain("P@ssw0rd!", responseBody);
        Assert.DoesNotContain("SecretService.cs", responseBody);
        Assert.DoesNotContain("stackTrace", responseBody, StringComparison.OrdinalIgnoreCase);

        using var jsonDoc = JsonDocument.Parse(responseBody);
        var root = jsonDoc.RootElement;
        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.Equal("Internal Server Error", root.GetProperty("title").GetString());
        Assert.Equal("An unexpected error occurred while processing your request. Please try again later.", root.GetProperty("detail").GetString());
    }
}
