namespace InsuranceClaimsTracker.Application.Common.Models;

public class LlmAdvisoryResult
{
    public bool IsSuccess { get; init; }
    public string? RawJson { get; init; }
    public string? ErrorMessage { get; init; }
    public int StatusCode { get; init; } = 200;

    public static LlmAdvisoryResult Success(string rawJson) =>
        new() { IsSuccess = true, RawJson = rawJson, StatusCode = 200 };

    public static LlmAdvisoryResult Failure(string errorMessage, int statusCode = 503) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage, StatusCode = statusCode };
}
