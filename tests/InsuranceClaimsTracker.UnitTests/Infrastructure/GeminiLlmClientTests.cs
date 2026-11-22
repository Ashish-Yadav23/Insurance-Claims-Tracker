using System.Net;
using InsuranceClaimsTracker.Application.Common.Models;
using InsuranceClaimsTracker.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InsuranceClaimsTracker.UnitTests.Infrastructure;

public class GeminiLlmClientTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Handler { get; set; } =
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"category\\\":\\\"WaterDamage\\\",\\\"summary\\\":\\\"Flooded floor.\\\",\\\"suggestedPriority\\\":\\\"Medium\\\"}\"}]}}]}")
            });

        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return await Handler(request, cancellationToken);
        }
    }

    private static GeminiLlmClient CreateClient(
        MockHttpMessageHandler handler,
        string apiKey = "test-api-key-12345",
        int timeoutSeconds = 5,
        int maxRetries = 1)
    {
        var httpClient = new HttpClient(handler);
        var settings = Options.Create(new AiServiceSettings
        {
            ApiKey = apiKey,
            Model = "gemini-2.5-flash",
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
            TimeoutSeconds = timeoutSeconds,
            MaxRetries = maxRetries
        });

        return new GeminiLlmClient(httpClient, settings, NullLogger<GeminiLlmClient>.Instance);
    }

    [Fact]
    public async Task GenerateClaimAdvisory_WhenApiKeyMissing_ReturnsConfigError503WithoutHttpCall()
    {
        var handler = new MockHttpMessageHandler();
        var client = CreateClient(handler, apiKey: "");

        var result = await client.GenerateClaimAdvisoryAsync(new LlmAdvisoryRequest
        {
            Description = "Test incident",
            PolicyType = "Auto",
            ClaimAmount = 1000m
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("not currently configured", result.ErrorMessage);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GenerateClaimAdvisory_OnSuccess_SendsHeaderAndReturnsCandidateText()
    {
        var handler = new MockHttpMessageHandler();
        var client = CreateClient(handler, apiKey: "secret-gemini-key");

        var result = await client.GenerateClaimAdvisoryAsync(new LlmAdvisoryRequest
        {
            Description = "Broken pipe flooded bathroom",
            PolicyType = "Home",
            ClaimAmount = 2000m
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.RawJson);
        Assert.Contains("WaterDamage", result.RawJson);

        Assert.Equal(1, handler.CallCount);
        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("x-goog-api-key"));
        var keyVal = handler.LastRequest.Headers.GetValues("x-goog-api-key").First();
        Assert.Equal("secret-gemini-key", keyVal);
    }

    [Fact]
    public async Task GenerateClaimAdvisory_WhenRateLimited429_RetriesAndReturns503IfExhausted()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests))
        };
        var client = CreateClient(handler, maxRetries: 1);

        var result = await client.GenerateClaimAdvisoryAsync(new LlmAdvisoryRequest
        {
            Description = "Test incident",
            PolicyType = "Health",
            ClaimAmount = 500m
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("rate limit was exceeded", result.ErrorMessage);
        // Initial attempt + 1 retry = 2 calls
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GenerateClaimAdvisory_WhenUnauthorized401_DoesNotRetry()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))
        };
        var client = CreateClient(handler, maxRetries: 2);

        var result = await client.GenerateClaimAdvisoryAsync(new LlmAdvisoryRequest
        {
            Description = "Test incident",
            PolicyType = "Life",
            ClaimAmount = 50000m
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("authentication failed", result.ErrorMessage);
        // Auth failures must NEVER be retried
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GenerateClaimAdvisory_WhenServer500_RetriesAndReturns503()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))
        };
        var client = CreateClient(handler, maxRetries: 1);

        var result = await client.GenerateClaimAdvisoryAsync(new LlmAdvisoryRequest
        {
            Description = "Hail damage",
            PolicyType = "Auto",
            ClaimAmount = 3000m
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("error processing the request", result.ErrorMessage);
        Assert.Equal(2, handler.CallCount);
    }
}
