using InsuranceClaimsTracker.Application.Common.Interfaces;
using InsuranceClaimsTracker.Application.Common.Models;

namespace InsuranceClaimsTracker.UnitTests.TestDoubles;

public class TestLlmClient : ILlmClient
{
    public LlmAdvisoryResult ResponseToReturn { get; set; } = LlmAdvisoryResult.Success(
        "{\"category\":\"PropertyDamage\",\"summary\":\"Windstorm damage to roof tiles.\",\"suggestedPriority\":\"Medium\"}");

    public Func<LlmAdvisoryRequest, CancellationToken, Task<LlmAdvisoryResult>>? Handler { get; set; }

    public LlmAdvisoryRequest? LastRequest { get; private set; }
    public int CallCount { get; private set; }

    public async Task<LlmAdvisoryResult> GenerateClaimAdvisoryAsync(
        LlmAdvisoryRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastRequest = request;

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (Handler != null)
        {
            return await Handler(request, cancellationToken);
        }

        return ResponseToReturn;
    }
}
