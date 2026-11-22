using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InsuranceClaimsTracker.Application.DTOs.Admin;
using InsuranceClaimsTracker.Application.DTOs.Auth;
using InsuranceClaimsTracker.Application.DTOs.Claims;
using InsuranceClaimsTracker.Application.DTOs.Policies;
using InsuranceClaimsTracker.Domain.Entities;
using InsuranceClaimsTracker.Domain.Enums;
using InsuranceClaimsTracker.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InsuranceClaimsTracker.IntegrationTests;

[Collection("SqlServerEndToEndCollection")]
public class EndToEndBusinessWorkflowTests : IClassFixture<SqlServerWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly SqlServerWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public EndToEndBusinessWorkflowTests(SqlServerWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CompleteBusinessWorkflow_CustomerAndAdmin_ExecutesSuccessfullyThroughHttp()
    {
        // -------------------------------------------------------------------------
        // 1. Register Customer via POST /api/v1/auth/register
        // -------------------------------------------------------------------------
        var customerEmail = $"workflow_cust_{Guid.NewGuid():N}@example.com";
        var customerPassword = "CustomerSecurePass123!";
        var registerRequest = new RegisterRequestDto
        {
            Name = "John Doe Customer",
            Email = customerEmail,
            Password = customerPassword
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var registeredCustomer = await registerResponse.Content.ReadFromJsonAsync<UserProfileDto>(JsonOptions);
        Assert.NotNull(registeredCustomer);
        Assert.True(registeredCustomer.Id > 0);
        Assert.Equal("Customer", registeredCustomer.Role);
        var customerId = registeredCustomer.Id;

        // -------------------------------------------------------------------------
        // 2. Authenticate Customer and receive JWT via POST /api/v1/auth/login
        // -------------------------------------------------------------------------
        var customerLoginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            Email = customerEmail,
            Password = customerPassword
        });
        Assert.Equal(HttpStatusCode.OK, customerLoginResponse.StatusCode);
        var customerAuth = await customerLoginResponse.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        Assert.NotNull(customerAuth);
        Assert.False(string.IsNullOrWhiteSpace(customerAuth.Token));
        var customerJwt = customerAuth.Token;

        // -------------------------------------------------------------------------
        // 3. Provision an Admin through trusted test setup
        // -------------------------------------------------------------------------
        var adminEmail = $"workflow_admin_{Guid.NewGuid():N}@example.com";
        var adminPassword = "AdminSecurePassword123!";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<InsuranceClaimsTracker.Application.Common.Interfaces.IPasswordHasherService>();
            var adminUser = new User(
                "Jane Smith Admin",
                adminEmail,
                passwordHasher.HashPassword(adminPassword),
                UserRole.Admin);
            db.Users.Add(adminUser);
            await db.SaveChangesAsync();
        }

        // -------------------------------------------------------------------------
        // 4. Authenticate Admin and receive JWT via POST /api/v1/auth/login
        // -------------------------------------------------------------------------
        var adminLoginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            Email = adminEmail,
            Password = adminPassword
        });
        Assert.Equal(HttpStatusCode.OK, adminLoginResponse.StatusCode);
        var adminAuth = await adminLoginResponse.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        Assert.NotNull(adminAuth);
        Assert.False(string.IsNullOrWhiteSpace(adminAuth.Token));
        var adminJwt = adminAuth.Token;
        var adminId = adminAuth.User.Id;

        // -------------------------------------------------------------------------
        // 5. Admin creates a policy for Customer via POST /api/v1/policies
        // -------------------------------------------------------------------------
        var policyNumber = $"POL-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant();
        var createPolicyRequest = new CreatePolicyRequestDto
        {
            PolicyNumber = policyNumber,
            CustomerId = customerId,
            PolicyType = PolicyType.Auto,
            CoverageAmount = 30000m,
            StartDate = DateTimeOffset.UtcNow.AddMonths(-2),
            EndDate = DateTimeOffset.UtcNow.AddMonths(10)
        };

        using var adminCreatePolicyReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/policies")
        {
            Content = JsonContent.Create(createPolicyRequest)
        };
        adminCreatePolicyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);

        var createPolicyResponse = await _client.SendAsync(adminCreatePolicyReq);
        Assert.Equal(HttpStatusCode.Created, createPolicyResponse.StatusCode);
        var createdPolicy = await createPolicyResponse.Content.ReadFromJsonAsync<PolicyResponseDto>(JsonOptions);
        Assert.NotNull(createdPolicy);
        Assert.Equal(policyNumber, createdPolicy.PolicyNumber);
        var policyId = createdPolicy.Id;

        // -------------------------------------------------------------------------
        // 6. Customer retrieves their own policy via GET /api/v1/policies/{id}
        // -------------------------------------------------------------------------
        using var customerGetPolicyReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/policies/{policyId}");
        customerGetPolicyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerJwt);

        var getPolicyResponse = await _client.SendAsync(customerGetPolicyReq);
        Assert.Equal(HttpStatusCode.OK, getPolicyResponse.StatusCode);
        var retrievedPolicy = await getPolicyResponse.Content.ReadFromJsonAsync<PolicyResponseDto>(JsonOptions);
        Assert.NotNull(retrievedPolicy);
        Assert.Equal(policyId, retrievedPolicy.Id);
        Assert.Equal(policyNumber, retrievedPolicy.PolicyNumber);

        // -------------------------------------------------------------------------
        // 7. Customer submits a valid claim via POST /api/v1/claims
        // -------------------------------------------------------------------------
        var claimAmount = 4500m;
        var incidentDate = DateTimeOffset.UtcNow.AddDays(-10);
        var createClaimRequest = new CreateClaimRequestDto
        {
            PolicyId = policyId,
            ClaimAmount = claimAmount,
            IncidentDate = incidentDate,
            Description = "Front bumper damage from minor collision in parking lot."
        };

        using var customerSubmitClaimReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims")
        {
            Content = JsonContent.Create(createClaimRequest)
        };
        customerSubmitClaimReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerJwt);

        var submitClaimResponse = await _client.SendAsync(customerSubmitClaimReq);
        Assert.Equal(HttpStatusCode.Created, submitClaimResponse.StatusCode);
        var createdClaim = await submitClaimResponse.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(createdClaim);
        Assert.Equal("Submitted", createdClaim.Status);
        Assert.NotNull(createdClaim.RowVersion);
        var claimId = createdClaim.Id;

        // -------------------------------------------------------------------------
        // 8. Customer retrieves the claim and initial audit history via GET /api/v1/claims/{id}
        // -------------------------------------------------------------------------
        using var customerGetClaimReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/claims/{claimId}");
        customerGetClaimReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerJwt);

        var getClaimResponse = await _client.SendAsync(customerGetClaimReq);
        Assert.Equal(HttpStatusCode.OK, getClaimResponse.StatusCode);
        var claimAfterSubmit = await getClaimResponse.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(claimAfterSubmit);
        Assert.Equal("Submitted", claimAfterSubmit.Status);
        Assert.Single(claimAfterSubmit.History);
        Assert.Null(claimAfterSubmit.History[0].PreviousStatus);
        Assert.Equal("Submitted", claimAfterSubmit.History[0].NewStatus);
        Assert.Equal(customerId, claimAfterSubmit.History[0].ChangedById);

        // -------------------------------------------------------------------------
        // 9. Admin starts claim review via POST /api/v1/admin/claims/{id}/start-review
        // -------------------------------------------------------------------------
        using var adminStartReviewReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claimId}/start-review")
        {
            Content = JsonContent.Create(new StartReviewRequestDto
            {
                RowVersion = claimAfterSubmit.RowVersion!,
                ReviewNote = "Assigned adjuster to inspect vehicle damage."
            })
        };
        adminStartReviewReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);

        var startReviewResponse = await _client.SendAsync(adminStartReviewReq);
        Assert.Equal(HttpStatusCode.OK, startReviewResponse.StatusCode);
        var claimUnderReview = await startReviewResponse.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(claimUnderReview);
        Assert.Equal("UnderReview", claimUnderReview.Status);
        Assert.NotNull(claimUnderReview.RowVersion);

        // -------------------------------------------------------------------------
        // 10. Admin approves the claim via POST /api/v1/admin/claims/{id}/decision
        // -------------------------------------------------------------------------
        var approvedAmount = 4200m;
        using var adminDecisionReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claimId}/decision")
        {
            Content = JsonContent.Create(new ClaimDecisionRequestDto
            {
                Decision = "Approved",
                ApprovedAmount = approvedAmount,
                ReviewNote = "Damage estimate verified with repair shop; claim approved minus deductible.",
                RowVersion = claimUnderReview.RowVersion!
            })
        };
        adminDecisionReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);

        var decisionResponse = await _client.SendAsync(adminDecisionReq);
        Assert.Equal(HttpStatusCode.OK, decisionResponse.StatusCode);
        var claimApproved = await decisionResponse.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(claimApproved);
        Assert.Equal("Approved", claimApproved.Status);
        Assert.Equal(approvedAmount, claimApproved.ApprovedAmount);
        Assert.NotNull(claimApproved.RowVersion);

        // -------------------------------------------------------------------------
        // 11. Admin records payout for the approved claim via POST /api/v1/admin/claims/{id}/payout
        // -------------------------------------------------------------------------
        var paymentReference = $"WIRE-{Guid.NewGuid():N}".Substring(0, 18).ToUpperInvariant();
        using var adminPayoutReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{claimId}/payout")
        {
            Content = JsonContent.Create(new ClaimPayoutRequestDto
            {
                PaymentReference = paymentReference,
                RowVersion = claimApproved.RowVersion!
            })
        };
        adminPayoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);

        var payoutResponse = await _client.SendAsync(adminPayoutReq);
        Assert.Equal(HttpStatusCode.OK, payoutResponse.StatusCode);
        var claimPaid = await payoutResponse.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(claimPaid);
        Assert.Equal("Paid", claimPaid.Status);
        Assert.Equal(paymentReference, claimPaid.PaymentReference);
        Assert.NotNull(claimPaid.PaidAt);

        // -------------------------------------------------------------------------
        // 12. Customer retrieves the final claim status and verifies chronological history
        // -------------------------------------------------------------------------
        using var customerFinalGetReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/claims/{claimId}");
        customerFinalGetReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerJwt);

        var finalResponse = await _client.SendAsync(customerFinalGetReq);
        Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);
        var finalClaim = await finalResponse.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(finalClaim);
        Assert.Equal("Paid", finalClaim.Status);
        Assert.Equal(approvedAmount, finalClaim.ApprovedAmount);
        Assert.Equal(paymentReference, finalClaim.PaymentReference);
        Assert.NotNull(finalClaim.PaidAt);

        // Chronological audit trail verification: exactly 4 records
        Assert.Equal(4, finalClaim.History.Count);

        // Record 1: Submitted by Customer
        var h1 = finalClaim.History[0];
        Assert.Null(h1.PreviousStatus);
        Assert.Equal("Submitted", h1.NewStatus);
        Assert.Equal(customerId, h1.ChangedById);

        // Record 2: UnderReview by Admin
        var h2 = finalClaim.History[1];
        Assert.Equal("Submitted", h2.PreviousStatus);
        Assert.Equal("UnderReview", h2.NewStatus);
        Assert.Equal(adminId, h2.ChangedById);
        Assert.Equal("Assigned adjuster to inspect vehicle damage.", h2.ReviewNote);

        // Record 3: Approved by Admin
        var h3 = finalClaim.History[2];
        Assert.Equal("UnderReview", h3.PreviousStatus);
        Assert.Equal("Approved", h3.NewStatus);
        Assert.Equal(adminId, h3.ChangedById);
        Assert.Contains("deductible", h3.ReviewNote);

        // Record 4: Paid by Admin
        var h4 = finalClaim.History[3];
        Assert.Equal("Approved", h4.PreviousStatus);
        Assert.Equal("Paid", h4.NewStatus);
        Assert.Equal(adminId, h4.ChangedById);
        Assert.Contains(paymentReference, h4.ReviewNote);

        // Strictly chronological ordering check
        Assert.True(h1.ChangedAt <= h2.ChangedAt);
        Assert.True(h2.ChangedAt <= h3.ChangedAt);
        Assert.True(h3.ChangedAt <= h4.ChangedAt);
    }

    [Fact]
    public async Task ClaimRejectionWorkflow_CustomerAndAdmin_ExecutesAndMaintainsChronologicalAuditTrail()
    {
        // 1. Register Customer
        var customerEmail = $"reject_cust_{Guid.NewGuid():N}@example.com";
        var customerPassword = "CustomerSecurePass123!";
        var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequestDto
        {
            Name = "Alice Rejection Test",
            Email = customerEmail,
            Password = customerPassword
        });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var customer = await registerResponse.Content.ReadFromJsonAsync<UserProfileDto>(JsonOptions);
        var customerId = customer!.Id;

        var loginRes = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            Email = customerEmail,
            Password = customerPassword
        });
        var customerAuth = await loginRes.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        var customerJwt = customerAuth!.Token;

        // 2. Provision Admin
        var adminEmail = $"reject_admin_{Guid.NewGuid():N}@example.com";
        var adminPassword = "AdminSecurePassword123!";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<InsuranceClaimsTracker.Application.Common.Interfaces.IPasswordHasherService>();
            db.Users.Add(new User("Admin Auditor", adminEmail, passwordHasher.HashPassword(adminPassword), UserRole.Admin));
            await db.SaveChangesAsync();
        }

        var adminLoginRes = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            Email = adminEmail,
            Password = adminPassword
        });
        var adminAuth = await adminLoginRes.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        var adminJwt = adminAuth!.Token;
        var adminId = adminAuth.User.Id;

        // 3. Admin creates Policy
        var policyNumber = $"POL-{Guid.NewGuid():N}".Substring(0, 15).ToUpperInvariant();
        using var createPolicyReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/policies")
        {
            Content = JsonContent.Create(new CreatePolicyRequestDto
            {
                PolicyNumber = policyNumber,
                CustomerId = customerId,
                PolicyType = PolicyType.Home,
                CoverageAmount = 50000m,
                StartDate = DateTimeOffset.UtcNow.AddMonths(-1),
                EndDate = DateTimeOffset.UtcNow.AddMonths(11)
            })
        };
        createPolicyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
        var policyRes = await _client.SendAsync(createPolicyReq);
        var policy = await policyRes.Content.ReadFromJsonAsync<PolicyResponseDto>(JsonOptions);

        // 4. Customer submits Claim
        using var submitClaimReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims")
        {
            Content = JsonContent.Create(new CreateClaimRequestDto
            {
                PolicyId = policy!.Id,
                ClaimAmount = 8000m,
                IncidentDate = DateTimeOffset.UtcNow.AddDays(-3),
                Description = "Roof leak during severe rainstorm."
            })
        };
        submitClaimReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerJwt);
        var submitRes = await _client.SendAsync(submitClaimReq);
        var submittedClaim = await submitRes.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(submittedClaim);
        Assert.NotNull(submittedClaim.RowVersion);

        // 5. Admin starts Review
        using var startReviewReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{submittedClaim.Id}/start-review")
        {
            Content = JsonContent.Create(new StartReviewRequestDto
            {
                RowVersion = submittedClaim.RowVersion,
                ReviewNote = "Commencing review on reported roof leak."
            })
        };
        startReviewReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
        var startReviewRes = await _client.SendAsync(startReviewReq);
        Assert.Equal(HttpStatusCode.OK, startReviewRes.StatusCode);
        var underReviewClaim = await startReviewRes.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);
        Assert.NotNull(underReviewClaim);
        Assert.NotNull(underReviewClaim.RowVersion);

        // 6. Admin Rejects Claim
        using var rejectReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/claims/{submittedClaim.Id}/decision")
        {
            Content = JsonContent.Create(new ClaimDecisionRequestDto
            {
                Decision = "Rejected",
                ReviewNote = "Inspection revealed pre-existing roof wear excluded from policy coverage.",
                RowVersion = underReviewClaim.RowVersion
            })
        };
        rejectReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
        var rejectRes = await _client.SendAsync(rejectReq);
        Assert.Equal(HttpStatusCode.OK, rejectRes.StatusCode);

        // 7. Customer retrieves final rejected claim
        using var getFinalReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/claims/{submittedClaim.Id}");
        getFinalReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerJwt);
        var finalRes = await _client.SendAsync(getFinalReq);
        var finalClaim = await finalRes.Content.ReadFromJsonAsync<ClaimResponseDto>(JsonOptions);

        Assert.NotNull(finalClaim);
        Assert.Equal("Rejected", finalClaim.Status);
        Assert.Null(finalClaim.ApprovedAmount);
        Assert.Null(finalClaim.PaymentReference);
        Assert.Null(finalClaim.PaidAt);

        // Audit Trail: Exactly 3 records
        Assert.Equal(3, finalClaim.History.Count);
        Assert.Equal("Submitted", finalClaim.History[0].NewStatus);
        Assert.Equal(customerId, finalClaim.History[0].ChangedById);

        Assert.Equal("UnderReview", finalClaim.History[1].NewStatus);
        Assert.Equal(adminId, finalClaim.History[1].ChangedById);

        Assert.Equal("Rejected", finalClaim.History[2].NewStatus);
        Assert.Equal(adminId, finalClaim.History[2].ChangedById);
        Assert.Contains("pre-existing", finalClaim.History[2].ReviewNote);

        // Chronological check
        Assert.True(finalClaim.History[0].ChangedAt <= finalClaim.History[1].ChangedAt);
        Assert.True(finalClaim.History[1].ChangedAt <= finalClaim.History[2].ChangedAt);
    }
}
