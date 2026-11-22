# Phased Implementation Plan

This implementation plan breaks the **Insurance Claims Tracker** into 10 incremental, highly verifiable phases. Each phase defines specific deliverables, strict acceptance criteria, verification commands, and interview defense topics.

---

## Phase 1: Solution Setup, Project References, API Host & Base Domain

### Goal
Establish a buildable .NET solution with the four layered projects and two test projects, correct project references, API startup as the composition root, dependency injection setup, Swagger/OpenAPI, a health check endpoint, configuration templates, `.gitignore`, initial domain models, and baseline unit tests.

### Project Dependencies to Configure
- `Domain`: No references to other projects.
- `Application`: References `Domain`.
- `Infrastructure`: References `Application` and `Domain`.
- `Api`: References `Application` and `Infrastructure` (Composition Root).
- `UnitTests`: References `Domain`, `Application`, `Infrastructure`.
- `IntegrationTests`: References `Api`, `Application`, `Infrastructure`.

### Tasks
1. Initialize the solution and six projects:
   - `src/InsuranceClaimsTracker.Domain`
   - `src/InsuranceClaimsTracker.Application`
   - `src/InsuranceClaimsTracker.Infrastructure`
   - `src/InsuranceClaimsTracker.Api`
   - `tests/InsuranceClaimsTracker.UnitTests`
   - `tests/InsuranceClaimsTracker.IntegrationTests`
2. Configure project references adhering to Clean Architecture rules.
3. Configure solution files, `.gitignore` (filtering `bin/`, `obj/`, `.vs/`, `node_modules/`, `user-secrets/`), and configuration templates:
   - `appsettings.json` and `appsettings.Development.json` with configuration sections for `ConnectionStrings`, `JwtSettings`, and `AiService`.
4. Configure `InsuranceClaimsTracker.Api/Program.cs`:
   - Register dependency injection service extension hooks (`AddApplication()`, `AddInfrastructure()`).
   - Configure ASP.NET Core Health Checks mapping `GET /health`.
   - Configure OpenAPI / Swagger UI at `/swagger`.
5. Implement Domain Enums and Core Entities:
   - Enums: `UserRole` (`Customer = 1`, `Admin = 2`), `PolicyType` (`Health = 1`, `Auto = 2`, `Home = 3`, `Life = 4`), `ClaimStatus` (`Submitted = 1`, `UnderReview = 2`, `Approved = 3`, `Rejected = 4`, `Paid = 5`).
   - Entities: `User`, `Policy`, `Claim`, `ClaimHistory`.
   - Domain Exceptions: `InvalidClaimStateException`, `PolicyExpiredException`, `CoverageExceededException`, `UnauthorizedResourceAccessException`.
6. Implement baseline unit tests in `InsuranceClaimsTracker.UnitTests`:
   - Verify entity default properties, enum values, and domain exception instantiation.

### Acceptance Criteria
- Solution builds with zero errors and zero warnings across all projects.
- Project reference graph strictly validates that `Domain` has no dependencies on any other application project.
- Running `dotnet test` executes and passes all baseline unit tests.
- Launching the API responds with HTTP 200 `{"status": "Healthy"}` at `/health` and renders Swagger UI at `/swagger`.

### Verification Commands
```bash
dotnet build
dotnet test tests/InsuranceClaimsTracker.UnitTests
```

---

## Phase 2: EF Core Infrastructure, Database Configurations & Migrations [Completed]

### Goal
Configure Entity Framework Core with SQL Server, define table configurations with precise column constraints, SQL check constraints, `RowVersion` concurrency tokens, and generate and apply the initial migration to LocalDB.

### Tasks
1. Add EF Core 10.0.14 NuGet packages:
   - `Microsoft.EntityFrameworkCore` in `Application`
   - `Microsoft.EntityFrameworkCore.SqlServer` in `Infrastructure`
   - `Microsoft.EntityFrameworkCore.Design` in `Infrastructure` and `Api`
   - Local `dotnet-ef` tool manifest installed at version `10.0.14`
2. Define `IApplicationDbContext` in `Application/Common/Interfaces/IApplicationDbContext.cs` and implement it in `Infrastructure/Persistence/ApplicationDbContext.cs`.
3. Implement EF Core Fluent API Configurations (`IEntityTypeConfiguration<T>`):
   - Primary keys and identity columns.
   - Column types and lengths (`NVARCHAR(100)`, `NVARCHAR(256)`, `NVARCHAR(2000)`).
   - Decimal precision: `decimal(18,2)` for `CoverageAmount` and `ClaimAmount`.
   - SQL check constraints for individual-row invariants:
     - `CoverageAmount > 0` (`CK_Policies_CoverageAmount`)
     - `ClaimAmount > 0` (`CK_Claims_ClaimAmount`)
     - `EndDate > StartDate` (`CK_Policies_EndDate`)
     - `Role IN (1, 2)` (`CK_Users_Role`)
     - `PolicyType IN (1, 2, 3, 4)` (`CK_Policies_PolicyType`)
     - `Status IN (1, 2, 3, 4, 5)` (`CK_Claims_Status`)
     - `PreviousStatus IS NULL OR PreviousStatus IN (1, 2, 3, 4, 5)` (`CK_ClaimHistory_PreviousStatus`)
     - `NewStatus IN (1, 2, 3, 4, 5)` (`CK_ClaimHistory_NewStatus`)
   - Foreign keys and restrictive delete rules (`DeleteBehavior.Restrict`):
     - `Policies.CustomerId` -> `Users.Id`
     - `Claims.PolicyId` -> `Policies.Id`
     - `ClaimHistory.ClaimId` -> `Claims.Id`
     - `ClaimHistory.ChangedById` -> `Users.Id`
   - Concurrency token on `Claims`: `.IsRowVersion()` on `RowVersion`.
4. Configure development connection string in `appsettings.Development.json` for `(localdb)\MSSQLLocalDB`.
5. Generate the initial EF Core migration (`20261030155657_InitialCreate`) and apply to `InsuranceClaimsTrackerDb`.
6. Implement unit tests in `InsuranceClaimsTracker.UnitTests/Infrastructure/ApplicationDbContextModelTests.cs` verifying model metadata, constraints, precision, and delete behaviors without EF InMemory provider.

### Acceptance Criteria
- Migration compiles cleanly without warnings or errors.
- `dotnet ef database update` succeeds on local SQL Server LocalDB (`InsuranceClaimsTrackerDb`).
- Generated database schema includes all unique indexes, foreign keys, and SQL check constraints.
- Seed data policy safely defers user/policy/claim demo data to Phase 3 when PBKDF2 password hashing is available.
- All unit and integration test suites pass (38 passed, 0 failed).

### Verification Commands
```bash
dotnet restore InsuranceClaimsTracker.sln
dotnet build InsuranceClaimsTracker.sln
dotnet test InsuranceClaimsTracker.sln
dotnet ef migrations list --project src/InsuranceClaimsTracker.Infrastructure/InsuranceClaimsTracker.Infrastructure.csproj --startup-project src/InsuranceClaimsTracker.Api/InsuranceClaimsTracker.Api.csproj
dotnet ef database update --project src/InsuranceClaimsTracker.Infrastructure/InsuranceClaimsTracker.Infrastructure.csproj --startup-project src/InsuranceClaimsTracker.Api/InsuranceClaimsTracker.Api.csproj
```

---

## Phase 3: Identity, Password Hashing, JWT Auth & User Endpoints

### Goal
Implement secure customer registration, login credential verification, PBKDF2 password hashing, role-based JWT issuance, and the authentication API controller.

### Tasks
1. Implement `IPasswordHasher` in `Infrastructure` using `Microsoft.AspNetCore.Identity.PasswordHasher<User>`.
2. Implement `IJwtTokenGenerator` using `System.IdentityModel.Tokens.Jwt` with HMAC-SHA256 (`HS256`), signing tokens with user ID, email, and role claims.
3. Implement `AuthService` in `Application`:
   - `RegisterAsync`: Strictly forces `UserRole.Customer`. Rejects duplicate emails.
   - `LoginAsync`: Validates credentials against password hash; generates JWT token.
4. Implement `AuthController` in `Api`:
   - `POST /api/v1/auth/register` (Public)
   - `POST /api/v1/auth/login` (Public)
   - `GET /api/v1/auth/me` (Authenticated: Customer or Admin)
5. Configure JWT Bearer authentication and role authorization policies in `Program.cs`.
6. Implement unit tests in `InsuranceClaimsTracker.UnitTests`:
   - Password hasher salt generation and verification.
   - JWT token claim extraction and expiration.
   - Registration privilege escalation defense (verifying role is always set to `Customer`).

### Acceptance Criteria
- Public registration payload does not accept a role; registered accounts always have `Role = Customer`.
- Invalid login credentials return HTTP 401 Unauthorized.
- Successful login returns HTTP 200 OK with a valid JWT token.
- Passwords in the database are hashed with unique salts.
- All Phase 3 unit tests pass.

### Verification Commands
```bash
dotnet build
dotnet test tests/InsuranceClaimsTracker.UnitTests --filter "FullyQualifiedName~Auth"
```

---

## Phase 4: Policy Management & Customer Claim Submission

### Goal
Implement policy retrieval, admin policy issuance, and customer claim submission workflows with strict server-side ownership enforcement and eligibility validation.

### Tasks
1. Implement `PolicyService` and `PoliciesController`:
   - `GET /api/v1/policies`: Customers retrieve only their own policies (`CustomerId == currentUserId`); Admins can view all policies.
   - `GET /api/v1/policies/{id}`: Enforces server-side ownership check.
   - `POST /api/v1/policies`: Admin-only endpoint to issue policies.
2. Implement `ClaimService` and `ClaimsController`:
   - `POST /api/v1/claims`: Customer files a claim. Validates:
     - Target policy belongs to `currentUserId`.
     - Target policy is active (`StartDate <= IncidentDate <= EndDate`).
     - `IncidentDate <= DateTimeOffset.UtcNow`.
     - `ClaimAmount <= Policy.CoverageAmount`.
   - Automatically initializes claim status as `Submitted`.
   - Atomically records initial append-only record in `ClaimHistory`.
   - `GET /api/v1/claims`: Customers view only their claims; Admins view all claims.
   - `GET /api/v1/claims/{id}`: Returns claim details if owned by caller or if caller is Admin.
3. Implement FluentValidation validators for all incoming DTOs.
4. Implement unit tests in `InsuranceClaimsTracker.UnitTests`:
   - Customer claim submission business rule validation (coverage ceiling, date ranges).
   - Server-side ownership verification logic.
   - Initial `ClaimHistory` creation logic.

### Acceptance Criteria
- Public or customer users cannot issue policies (HTTP 403 Forbidden).
- Admins can issue policies to valid customers with validated coverage amounts and date ranges (HTTP 201 Created).
- Duplicate policy numbers return HTTP 409 Conflict.
- Customers can view only their own policies; accessing other customer policies returns HTTP 404 Not Found.
- Customers can submit claims only against their own active policies within the coverage date range (HTTP 201 Created).
- Attempting to submit a claim for an amount exceeding coverage or with invalid incident date returns HTTP 400 Bad Request.
- Submitted claims receive initial status `Submitted` and an atomic initial `ClaimHistory` entry with the customer ID.
- Customers cannot access other customers' claims (HTTP 404 Not Found); Admins can view claims across customers.
- Unauthenticated requests return HTTP 401 Unauthorized.
- All Phase 4 unit and integration tests pass.

### Verification Commands
```bash
dotnet restore InsuranceClaimsTracker.sln
dotnet build InsuranceClaimsTracker.sln
dotnet test InsuranceClaimsTracker.sln
```

---

## Phase 5: Admin Claim Review State Machine & Concurrency Handling (Completed)

### Goal
Implement administrative review, decision (approve/reject), and payout workflows with mandatory review notes, append-only audit tracking, and EF Core optimistic concurrency conflict handling.

### Completed Tasks
1. Implemented Claim State Machine in `Claim` domain entity:
   - `StartReview(int adminId, string? reviewNote)`: Validates transition `Submitted` -> `UnderReview`.
   - `Approve(int adminId, decimal approvedAmount, string reviewNote)`: Validates transition `UnderReview` -> `Approved` (note & valid amount required, `approvedAmount <= ClaimAmount`).
   - `Reject(int adminId, string reviewNote)`: Validates transition `UnderReview` -> `Rejected` (note required, clears `ApprovedAmount`).
   - `RecordPayout(int adminId, string paymentReference, DateTimeOffset? paidAt)`: Validates transition `Approved` -> `Paid` (prevents duplicate payouts).
2. Ensured every status transition appends an immutable record to `ClaimHistory` (`PreviousStatus`, `NewStatus`, `ChangedById`, `ChangedAt`, `ReviewNote`).
3. Applied EF Core migration `AddPayoutAndApprovalFieldsToClaim` adding `ApprovedAmount`, `PaymentReference`, `PaidAt`, and `CK_Claims_ApprovedAmount` constraint to `Claims` table.
4. Concurrency control:
   - Client sends base64-encoded `RowVersion` in all modification requests.
   - EF Core optimistic concurrency checking with `RowVersion` sequence comparison and `DbUpdateConcurrencyException` mapping to HTTP 409 Conflict.
5. Implemented `AdminClaimsController` in `Api`:
   - `POST /api/v1/admin/claims/{id}/start-review`
   - `POST /api/v1/admin/claims/{id}/decision`
   - `POST /api/v1/admin/claims/{id}/payout`
6. Implemented unit tests in `InsuranceClaimsTracker.UnitTests` and integration tests in `InsuranceClaimsTracker.IntegrationTests`:
   - Domain state transitions and invariants.
   - Admin claim service handling authorization, input validation, and concurrency conflicts.
   - End-to-end HTTP integration tests for all admin endpoints.
7. Conducted live runtime HTTP API and SQL Server database verification.

### Acceptance Criteria
- Invalid state transitions throw `InvalidClaimStateException` (HTTP 400).
- Approving or rejecting without a review note fails validation.
- Payout is strictly disallowed on claims not in `Approved` status; duplicate payout is rejected.
- Concurrent updates to the same claim trigger concurrency conflict handling (HTTP 409).
- All unit and integration tests pass (192 passing tests).

### Verification Commands
```bash
dotnet build
dotnet test tests/InsuranceClaimsTracker.UnitTests --filter "FullyQualifiedName~Review"
```

---

## Phase 6: Cross-Cutting Middleware, Integration, Concurrency & Regression Testing (Completed)

### Goal
Implement global RFC 7807 exception handling middleware, request logging, and build an end-to-end automated integration and regression test suite using `WebApplicationFactory`.

### Completed Tasks
1. Implemented centralized `ExceptionHandlingMiddleware` in `Api`:
   - Registered ASP.NET Core `ProblemDetails` services in `Program.cs`.
   - Maps `CoverageExceededException`, `PolicyExpiredException`, `InvalidClaimStateException`, `DomainException`, `ValidationException`, `ArgumentException` -> `400 Bad Request`.
   - Maps `KeyNotFoundException` and `UnauthorizedResourceAccessException` -> `404 Not Found` (preserving BOLA defense).
   - Maps `DbUpdateConcurrencyException` -> `409 Conflict`.
   - Maps known unique constraint violations (SQL Server 2601 / 2627 for email and policy number) -> `409 Conflict`.
   - Sanitizes unhandled exceptions -> `500 Internal Server Error` with generic client-safe messages (never exposing stack traces, DB credentials, or connection strings).
   - Enriches all problem details with `status`, `title`, `detail`, `instance`, `traceId`, and backward-compatible `message`.
2. Standardized API Controller responses:
   - Configured `InvalidModelStateResponseFactory` in `Program.cs` for consistent RFC 7807 validation responses.
   - Added `ApiResultExtensions.ToProblemDetails` across `AuthController`, `PoliciesController`, `ClaimsController`, and `AdminClaimsController`.
3. Created test infrastructure:
   - `CustomWebApplicationFactory`: Fast in-memory database testing for core endpoints and routing.
   - `SqlServerWebApplicationFactory`: Real SQL Server LocalDB integration testing with dedicated isolated databases (`InsuranceClaimsTracker_ConcurrencyTest_<guid>`) and native `rowversion` / `timestamp` enforcement.
   - Zero interference with the development database `InsuranceClaimsTrackerDb`.
4. Automated test suites created:
   - `ExceptionHandlingMiddlewareTests.cs` (Unit tests): Verifies all exception mappings, Content-Type headers, ProblemDetails JSON output, and stack trace sanitization.
   - `EndToEndBusinessWorkflowTests.cs` (Integration tests): Full 12-step customer and admin HTTP journey (Register -> Authenticate -> Provision Admin -> Authenticate Admin -> Create Policy -> Get Policy -> Submit Claim -> Get Claim & History -> Start Review -> Approve -> Payout -> Verify Chronological History) and independent claim rejection workflow.
   - `SecurityRegressionTests.cs` (Integration tests): Comprehensive authentication (missing, malformed, expired, invalid signature), authorization (role enforcement), and BOLA/IDOR protection.
   - `SqlServerConcurrencyRegressionTests.cs` (Integration tests): Deterministic concurrency testing against real SQL Server LocalDB verifying optimistic locking conflicts (HTTP 409), stale `RowVersion` detection, no orphaned `ClaimHistory` records, and rejection/payout state invariants.
5. Live runtime API verification:
   - Verified `/health`, `/swagger/index.html`, `/swagger/v1/swagger.json`, customer/admin registration, login, policy creation, claim filing, review, approval, payout, 401 unauthorized, 403 forbidden, 400 validation ProblemDetails, and 409 concurrency conflict against live Kestrel and LocalDB.

### Acceptance Criteria Verified
- All API exceptions return standard RFC 7807 `ProblemDetails` JSON.
- Integration tests verify BOLA protection and prevent unauthorized resource access.
- Concurrency integration tests verify optimistic locking and HTTP 409 response using native SQL Server `RowVersion`.
- Full test suite passes 100% cleanly (224 total tests: 167 unit tests, 57 integration tests).

### Verification Commands & Results
```bash
dotnet build InsuranceClaimsTracker.sln
# Build succeeded: 0 Warning(s) from code, 0 Error(s)

dotnet test InsuranceClaimsTracker.sln
# Passed! - Failed: 0, Passed: 167, Skipped: 0, Total: 167 (InsuranceClaimsTracker.UnitTests)
# Passed! - Failed: 0, Passed:  57, Skipped: 0, Total:  57 (InsuranceClaimsTracker.IntegrationTests)
# Total: 224 Passing Tests (100% Success)
```

---

## Phase 7: GenAI Advisory Service Integration [Completed]

### Goal
Integrate an LLM service (Google Gemini REST API / `gemini-2.5-flash`) behind vendor-neutral abstractions to provide structured advisory claim categorization, concise summarization, and suggested priority levels for administrators triaging claims, with complete data minimization, error resilience, and zero mutation of core business invariants.

### Tasks
1. Define provider-agnostic abstractions and contracts in `Application`:
   - `ILlmClient`: Vendor-neutral LLM client abstraction (`GenerateAdvisoryAsync(LlmAdvisoryRequest, CancellationToken)`).
   - `IClaimAdvisoryService`: Advisory service contract (`GetAdvisoryAsync(int claimId, CancellationToken)`).
   - Models: `LlmAdvisoryRequest`, `LlmAdvisoryResult`, `ClaimAdvisoryConstants` (allowed categories: `Medical`, `AutoAccident`, `PropertyDamage`, `WaterDamage`, `Theft`, `Life`, `Other`; allowed priorities: `Low`, `Medium`, `High`; max summary length: 1,000 characters).
   - Response DTO: `ClaimAdvisoryResponseDto` (`category`, `summary`, `suggestedPriority`).
2. Implement orchestration and business invariants in `Application/Services/ClaimAdvisoryService.cs`:
   - Enforce administrator authorization via `ICurrentUserContext`.
   - Retrieve claim, policy, and history directly from database; client-supplied descriptions are rejected.
   - Return 404 if the claim does not exist.
   - Return 400 Bad Request if the claim description is empty or whitespace.
   - Enforce PII data minimization: send only policy type name, claim amount, incident date, and description. Customer name, email, password hash, policy numbers, bank details, and JWT tokens are strictly omitted.
   - Wrap untrusted descriptions in delimiter tags (`<claim_description>...</claim_description>`) to mitigate prompt injection.
   - Strip markdown formatting from model responses and validate output against allowed category enums, priorities, and length bounds.
   - Ensure advisory output is transient: zero updates to `Claim.Status`, `ApprovedAmount`, `PaymentReference`, `PaidAt`, `RowVersion`, or `ClaimHistory`.
3. Implement provider client in `Infrastructure/Ai/GeminiLlmClient.cs`:
   - Use typed `HttpClient` configured via `AiServiceSettings` (`Provider: "Gemini"`, `Model: "gemini-2.5-flash"`, `BaseUrl: "https://generativelanguage.googleapis.com/v1beta"`).
   - Pass API key securely via `x-goog-api-key` HTTP header (never query parameters or URL logs).
   - Enforce structured JSON schema (`response_mime_type: "application/json"`, `response_schema`).
   - Propagate cancellation tokens with configurable request timeout (15s default).
   - Implement exponential backoff retries for transient provider failures (HTTP 429 and 5xx).
   - Fail immediately without retry on client authentication errors (HTTP 401/403).
   - Return clean failure results mapped to RFC 7807 `503 Service Unavailable` ProblemDetails when the provider is unconfigured or unavailable.
4. Add endpoint `POST /api/v1/admin/claims/{id}/advisory` in `AdminClaimsController`:
   - Guarded with `[Authorize(Roles = "Admin")]`.
   - Maps `ServiceResult<ClaimAdvisoryResponseDto>` to HTTP responses (200, 400, 401, 403, 404, 503).
5. Comprehensive unit testing (23 new tests in `InsuranceClaimsTracker.UnitTests`):
   - `ClaimAdvisoryServiceTests` (18 tests): Category normalization across all types (WaterDamage, Theft, Medical, AutoAccident), priority validation, markdown stripping, malformed JSON fallback, oversized summaries, empty description validation, provider timeout / 429 / 5xx handling, cancellation handling, non-admin 403, unauthenticated 401, missing claim 404, and database non-mutation verification.
   - `GeminiLlmClientTests` (5 tests): Missing API key 503, header authentication, 429 retry backoff, 401 non-retry, and 500 retry handling.
6. Integration testing consolidation (6 new integration tests in `InsuranceClaimsTracker.IntegrationTests`):
   - Created `MockIntegrationLlmClient` to guarantee zero paid or external calls during test suites.
   - Registered `MockIntegrationLlmClient` into `CustomWebApplicationFactory` and `SqlServerWebApplicationFactory`.
   - `ClaimAdvisoryEndpointTests`: Admin access, Customer 403, unauthenticated 401, missing claim 404, 503 provider failure handling, and claim status/audit trail immutability verification.

### Acceptance Criteria
- Solution builds cleanly with 0 errors.
- 253 total automated tests pass cleanly (190 UnitTests, 63 IntegrationTests).
- Live API runtime verification confirms:
  - Admin receives valid structured JSON advisory or 503 ProblemDetails when unconfigured.
  - Customer receives HTTP 403 Forbidden.
  - Unauthenticated caller receives HTTP 401 Unauthorized.
  - Non-existent claim returns HTTP 404 Not Found.
  - Claim status remains `Submitted`, history count remains `1`, and subsequent review/approval/payout workflow succeeds without regression.

### Verification Commands
```bash
dotnet build InsuranceClaimsTracker.sln
dotnet test InsuranceClaimsTracker.sln
```

---

## Phase 8: Frontend Foundation & Authentication UI

### Goal
Initialize the React single-page application with TypeScript, Vite, and Tailwind CSS, configure the API client with JWT interceptors, and implement customer login and registration views.

### Tasks
1. Initialize Vite project with React and TypeScript in `frontend/`:
   ```bash
   npm create vite@latest frontend -- --template react-ts
   ```
2. Configure Tailwind CSS and Lucide React icons.
3. Configure API client (`axios` / `fetch`) with:
   - Base URL configuration (`/api/v1`).
   - Request interceptor attaching `Authorization: Bearer <token>`.
   - Response interceptor handling `401 Unauthorized` (clearing session and redirecting to login).
4. Implement `AuthContext` managing authentication state, token persistence, and role decoding.
5. Implement authentication pages:
   - Login page (`/login`)
   - Register page (`/register`) — strictly provides customer registration fields (no role input).
6. Implement route guards: `ProtectedRoute` requiring authentication and `RoleRoute` requiring specific roles (`Customer` or `Admin`).

### Acceptance Criteria
- Frontend builds with zero TypeScript errors or ESLint warnings.
- Customer registration and login succeed against the live backend API.
- JWT is correctly stored and automatically injected into outbound API requests.

### Verification Commands
```bash
cd frontend
npm install
npm run build
```

---

## Phase 9: Frontend Customer & Admin Portals

### Goal
Build the customer dashboard, policy browser, claim submission workflow with dynamic coverage validation, and the administrator claims review, triage, and payout queue.

### Tasks
1. Implement Customer Portal:
   - Dashboard: Overview of active policies and status of recent claims.
   - My Policies: List coverage amounts, effective dates, and policy codes.
   - File Claim Modal/Form:
     - Real-time client-side validation (coverage ceiling indicator, valid incident dates).
     - Narrative description input.
   - Claim Tracking View: Visual step-by-step progress timeline (`Submitted` -> `UnderReview` -> `Approved` -> `Paid`).
   - Claim Audit History viewer.
2. Implement Admin Portal:
   - Claims Review Queue: Filterable claims table by Status, Date, and Policy Type.
   - Claim Detail & Triage View:
     - Policy and customer details.
     - "Generate AI Triage" advisory button displaying AI category suggestion and summary.
     - Status transition actions: "Begin Review", "Approve" (with note), "Reject" (with reason).
   - Payout Modal: Record payment reference number and finalize claim to `Paid`.
3. Implement responsive navigation and error banners displaying RFC 7807 problem details.

### Acceptance Criteria
- Customer can submit a claim and immediately view its updated status on their tracking timeline.
- Claim submission form dynamically enforces policy coverage maximums.
- Admin can triage claims, review AI suggestions, record review notes, approve/reject, and process payouts.
- Frontend builds cleanly with zero TypeScript errors.

### Verification Commands
```bash
cd frontend
npm run build
```

---

## Phase 10: Production Hardening, CI/CD & Deployment

### Goal
Containerize the API, generate idempotent database migration scripts, configure production rate limiting and security headers, and execute deployment to Azure App Service, Azure SQL, and Azure Static Web Apps.

### Tasks
1. Create multi-stage `Dockerfile` for the ASP.NET Core API.
2. Generate idempotent EF Core SQL migration script:
   ```bash
   dotnet ef migrations script --idempotent --output ./migrations.sql --project src/InsuranceClaimsTracker.Infrastructure --startup-project src/InsuranceClaimsTracker.Api
   ```
3. Configure production middleware:
   - ASP.NET Core Rate Limiting on authentication and AI endpoints.
   - Strict CORS policy matching the Azure Static Web Apps domain.
   - Security headers (HSTS, X-Content-Type-Options, X-Frame-Options).
4. Provision Azure resources (Azure SQL Serverless, App Service Linux .NET 10, Static Web Apps).
5. Configure GitHub Actions workflows for continuous integration and automated deployment.
6. Perform end-to-end smoke testing on production URLs.

### Acceptance Criteria
- Docker image builds and runs successfully.
- Migration script runs idempotently on fresh or existing databases.
- Production Web API and React frontend communicate securely over HTTPS with zero CORS errors.
- End-to-end customer and admin workflows succeed in production.

### Verification Commands
```bash
dotnet build -c Release
dotnet test
cd frontend && npm run build
```

---

## Related Documentation Links
- [System Architecture Overview](ARCHITECTURE.md)
- [Database Schema & State Transitions](DATABASE_SCHEMA.md)
- [API Contract & Endpoint Specifications](API_CONTRACT.md)
- [Security Architecture & Deployment Guide](SECURITY_AND_DEPLOYMENT.md)
