# Security Architecture & Deployment Guide

## 1. Security Architecture & Threat Mitigation

In an insurance and financial transaction application, data confidentiality, data integrity, and strict access control are paramount. The system implements defense-in-depth across the following layers.

---

### 1.1 Authentication & Registration Privilege Escalation Prevention
- **Registration Privilege Escalation Defense**:
  - The public registration endpoint (`POST /api/v1/auth/register`) accepts only `name`, `email`, and `password`.
  - The request DTO intentionally omits any `role` property. If an attacker supplies a `role` field in the raw JSON payload, the model binder ignores it, and the service layer explicitly sets:
    ```csharp
    user.Role = UserRole.Customer;
    ```
  - Administrative accounts cannot be registered via public endpoints. They are created exclusively through secure database seed configurations or authorized administrative management.
- **Password Hashing**:
  - Implemented using `Microsoft.AspNetCore.Identity.PasswordHasher<TUser>`, utilizing **PBKDF2 with HMAC-SHA512** with a 128-bit cryptographically random salt and 100,000+ iterations.
  - Plaintext passwords are never logged, cached, or persisted in memory beyond request execution.
- **JWT (JSON Web Token) Security**:
  - Tokens are signed with HMAC-SHA256 (`HS256`) using a key of at least 256 bits (32 bytes).
  - Configured with explicit `Issuer`, `Audience`, and a reasonable expiration window (e.g., 120 minutes).
  - Claims include `ClaimTypes.NameIdentifier` (user ID), `ClaimTypes.Email`, and `ClaimTypes.Role`.

---

### 1.2 Resource Ownership & BOLA / IDOR Defense
A common vulnerability in web applications is **Broken Object Level Authorization (BOLA)**, also known as **Insecure Direct Object Reference (IDOR)**, where a malicious customer accesses another user's policy or claim simply by altering the ID in the URL: `GET /api/v1/claims/502`.

#### Mitigation Strategy
1. **Controller-level role checks are insufficient**: `[Authorize(Roles = "Customer")]` only verifies that the caller is an authenticated customer.
2. **Context-aware service queries**:
   ```csharp
   public async Task<ClaimDetailsDto> GetClaimByIdAsync(int claimId, int currentUserId, string role)
   {
       var query = _context.Claims
           .Include(c => c.Policy)
           .Include(c => c.History)
           .AsQueryable();

       if (role == UserRoles.Customer)
       {
           // Restrict query strictly to claims whose policy belongs to the logged-in customer
           query = query.Where(c => c.Policy.CustomerId == currentUserId);
       }

       var claim = await query.FirstOrDefaultAsync(c => c.Id == claimId);
       if (claim == null)
       {
           // Return 404 rather than 403 to avoid disclosing the existence of another customer's record
           throw new NotFoundException("Claim", claimId);
       }

       return _mapper.Map<ClaimDetailsDto>(claim);
   }
   ```
3. **Claim Submission Validation**: When a customer files a claim, the backend queries the target policy with `p.Id == dto.PolicyId && p.CustomerId == currentUserId`. If no matching active policy is found, the request is rejected with `404 Not Found`.

---

### 1.3 Concurrency Control & Double-Payout Prevention
In financial operations, two administrators approving or executing a payout simultaneously could trigger duplicate disbursements.

#### Mitigation Strategy
- **Optimistic Concurrency**: The `Claims` entity features a `RowVersion` mapped via EF Core `.IsRowVersion()`.
- If Admin A and Admin B submit state modifications simultaneously, the second transaction fails with a `DbUpdateConcurrencyException`.
- The global `ExceptionHandlingMiddleware` intercepts `DbUpdateConcurrencyException` and maps it to HTTP `409 Conflict`, prompting the administrator to refresh their view.
- Payouts are guarded by atomic status checks:
  ```csharp
  if (claim.Status != ClaimStatus.Approved)
  {
      throw new InvalidClaimStateException("Claim must be in Approved status to record payout.");
  }
  ```

---

### 1.4 AI Safety, Guardrails & Governance
- **Sequence in Roadmap**: GenAI integration is deliberately scheduled after the core claim workflows, status transitions, role authorizations, and baseline integration test suites are fully verified.
- **Advisory Only**: AI suggestions are stored in `AiSuggestedCategory` and `AiSummary`. The LLM cannot execute state changes or approve disbursements.
- **Data Minimization**: Prompts sent to the LLM contain only the claim narrative, policy type, and claim amount. Personally Identifiable Information (PII) such as customer names, emails, and bank details are strictly omitted from prompt payloads.
- **Resilience**: The AI call is wrapped in a retry and fallback policy. If the LLM API is unavailable, the claim processing workflow continues uninterrupted, returning an advisory error status rather than failing the core workflow.

---

## 2. Secrets Management & Environment Configuration

Secrets must never be committed to source control.

| Environment | Secret Storage Mechanism | Configuration Source |
| :--- | :--- | :--- |
| **Local Development** | `dotnet user-secrets` | `secrets.json` mapped to `appsettings.Development.json` |
| **CI / Automated Tests** | GitHub Actions / Pipeline Secrets | Environment variables injected into test runners |
| **Azure Production** | Azure Key Vault / App Service App Settings | Environment variables (`WEBSITE_RUN_FROM_PACKAGE`) |

### Critical Configuration Keys

```bash
# Database Connection
ConnectionStrings__DefaultConnection="Server=(localdb)\\mssqllocaldb;Database=InsuranceClaimsTrackerDb;Trusted_Connection=True;MultipleActiveResultSets=true"

# JWT Security
JwtSettings__Secret="<At-least-32-characters-cryptographic-random-key>"
JwtSettings__Issuer="InsuranceClaimsTrackerApi"
JwtSettings__Audience="InsuranceClaimsTrackerClient"
JwtSettings__DurationInMinutes="120"

# AI Integration (configured in Phase 7)
AiService__Provider="Gemini" # or "AzureOpenAI"
AiService__ApiKey="<LLM-API-KEY>"
AiService__ModelId="gemini-2.5-flash"
```

---

## 3. Deployment Architecture

```mermaid
flowchart LR
    Browser["User Browser\n(Customer & Admin)"]
    SWA["Azure Static Web Apps\n(React + Vite + Tailwind)"]
    API["Azure App Service (Linux)\n(.NET 10 LTS Web API)"]
    DB[("Azure SQL Database\n(Serverless Tier)")]
    AI["LLM API\n(Gemini / Azure OpenAI)"]

    Browser -->|HTTPS| SWA
    Browser -->|REST API / JWT| API
    API -->|EF Core / TDS| DB
    API -->|HTTPS / JSON| AI
```

### Azure Deployment Prerequisites
1. **Azure Resource Group**: Logical container (e.g. `rg-insurance-tracker-prod`).
2. **Azure SQL Database**: Serverless tier with firewall allowing Azure services.
3. **Azure App Service**: Linux container running .NET 10 LTS runtime.
4. **Azure Static Web Apps**: Connects to the GitHub repository's `frontend/` directory for automated CI/CD deployment.
5. **CORS Policy**: Configured on the API to allow only the Static Web App domain.

---

## 4. Related Documentation Links
- [System Architecture Overview](ARCHITECTURE.md)
- [Database Schema & State Transitions](DATABASE_SCHEMA.md)
- [API Contract & Endpoint Specifications](API_CONTRACT.md)
- [Phased Implementation Plan](IMPLEMENTATION_PLAN.md)
