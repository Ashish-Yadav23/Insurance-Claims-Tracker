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
- Attempting to submit a claim for an amount exceeding coverage throws validation error.
- Attempting to submit a claim with an incident date outside the policy period fails validation.
- Customers cannot access or submit claims against policies they do not own.
- All Phase 4 unit tests pass.

### Verification Commands
```bash
dotnet build
dotnet test tests/InsuranceClaimsTracker.UnitTests --filter "FullyQualifiedName~Policy|FullyQualifiedName~Claim"
```

---

## Phase 5: Admin Claim Review State Machine & Concurrency Handling

### Goal
Implement administrative review, decision (approve/reject), and payout workflows with mandatory review notes, append-only audit tracking, and EF Core optimistic concurrency conflict handling.

### Tasks
1. Implement `ClaimReviewService` in `Application`:
   - `BeginReviewAsync(int claimId, int adminId, string note)`: Transitions `Submitted` -> `UnderReview`.
   - `ApproveClaimAsync(int claimId, int adminId, string reviewNote)`: Transitions `UnderReview` -> `Approved` (note required).
   - `RejectClaimAsync(int claimId, int adminId, string reviewNote)`: Transitions `UnderReview` -> `Rejected` (note required).
   - `RecordPayoutAsync(int claimId, int adminId, string payoutReference, string note)`: Transitions `Approved` -> `Paid`.
2. Ensure every status transition appends a record to `ClaimHistory` (`PreviousStatus`, `NewStatus`, `ChangedById`, `ChangedAt`, `ReviewNote`). Expose zero update or delete methods for `ClaimHistory`.
3. Handle concurrency:
   - When updating claims, EF Core includes `RowVersion` in the update check.
   - Catch `DbUpdateConcurrencyException` in application/API layers and propagate clean concurrency error.
4. Implement `AdminClaimsController` in `Api`:
   - `POST /api/v1/claims/{id}/review`
   - `POST /api/v1/claims/{id}/decision`
   - `POST /api/v1/claims/{id}/payout`
   - `GET /api/v1/claims/{id}/history`
5. Implement unit tests in `InsuranceClaimsTracker.UnitTests`:
   - State transition validation (disallow direct `Submitted` -> `Approved`, disallow payout on `Rejected` claims).
   - Mandatory review note validation.
   - Concurrency conflict handling simulation.

### Acceptance Criteria
- Invalid state transitions throw `InvalidClaimStateException`.
- Approving or rejecting without a review note fails validation.
- Payout is strictly disallowed on claims not in `Approved` status.
- Concurrent updates to the same claim trigger concurrency conflict handling (HTTP 409).
- All Phase 5 unit tests pass.

### Verification Commands
```bash
dotnet build
dotnet test tests/InsuranceClaimsTracker.UnitTests --filter "FullyQualifiedName~Review"
```

---

## Phase 6: Cross-Cutting Middleware, Integration, Concurrency & Regression Testing

### Goal
Implement global RFC 7807 exception handling middleware, request logging, and build an end-to-end automated integration and regression test suite using `WebApplicationFactory`.

### Tasks
1. Implement `ExceptionHandlingMiddleware` in `Api`:
   - Maps `ValidationException` -> 400 Bad Request
   - Maps `NotFoundException` -> 404 Not Found
   - Maps `UnauthorizedResourceAccessException` -> 404 Not Found (or 403)
   - Maps `InvalidClaimStateException` -> 400 Bad Request
   - Maps `DbUpdateConcurrencyException` -> 409 Conflict
   - Unhandled exceptions -> 500 Internal Server Error (without leaking stack traces in production).
2. Configure `InsuranceClaimsTracker.IntegrationTests`:
   - Set up custom `WebApplicationFactory<Program>` with in-memory database or SQLite.
3. Write comprehensive integration tests:
   - Auth flow: register customer, verify role is Customer, login, retrieve profile.
   - RBAC & BOLA defense: Customer A cannot access Customer B's policy or claim via GET routes.
   - Claim lifecycle flow: Submit -> BeginReview -> Approve -> Payout.
   - Concurrency test: simulate two simultaneous review requests on the same claim and verify one succeeds while the second receives HTTP 409 Conflict.
   - Audit trail verification: verify complete `ClaimHistory` records after full lifecycle.

### Acceptance Criteria
- All API exceptions return standard RFC 7807 `ProblemDetails` JSON.
- Integration tests verify BOLA protection and prevent unauthorized resource access.
- Concurrency integration tests verify optimistic locking and HTTP 409 response.
- Full test suite passes 100% cleanly.

### Verification Commands
```bash
dotnet build
dotnet test tests/InsuranceClaimsTracker.IntegrationTests
```

---

## Phase 7: GenAI Advisory Service Integration

### Goal
Integrate an LLM service (Gemini / Azure OpenAI API) to provide advisory claim categorization and triage summaries for claims adjusters, with fallback resilience.

### Tasks
1. Define `IClaimAiAdvisoryService` in `Application`.
2. Implement `ClaimAiAdvisoryService` in `Infrastructure` using `HttpClient` with typed structured JSON prompts:
   - Inputs: Policy type, claim description, claim amount.
   - Outputs: `suggestedCategory`, `summary`, `riskAssessment`.
3. Wrap LLM calls in retry/timeout policies with graceful degradation:
   - If the LLM provider fails or times out, the service logs the event and returns an advisory fallback response without interrupting or crashing the claims workflow.
4. Add endpoint `POST /api/v1/claims/{id}/ai-triage` in `AdminClaimsController`.
5. Store advisory results in `Claims.AiSuggestedCategory` and `Claims.AiSummary`.
6. Implement unit tests with mocked HTTP handlers validating prompt construction, JSON response parsing, and resilience under API failure.

### Acceptance Criteria
- AI suggestions remain strictly advisory and do not automatically transition claim status.
- Upstream AI service failures return an informative HTTP 502 or fallback response without server crashes.
- All Phase 7 unit tests pass.

### Verification Commands
```bash
dotnet build
dotnet test tests/InsuranceClaimsTracker.UnitTests --filter "FullyQualifiedName~Ai"
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
