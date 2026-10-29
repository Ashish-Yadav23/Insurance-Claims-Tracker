# System Architecture Overview

## 1. Architectural Style & Design Principles

The **Insurance Claims Tracker** backend follows a **Clean, Layered Architecture** (Domain-Centric / Onion hybrid), designed to adhere strictly to **SOLID** principles without introducing unnecessary indirection or framework bloat.

### Key Principles
1. **Separation of Concerns (SoC)**: Business logic is decoupled from transport layers (HTTP/API) and data access layers (EF Core/SQL Server).
2. **Dependency Inversion Principle (DIP)**: Core business abstractions do not depend on external libraries, frameworks, or database providers. Outer layers depend on inner abstractions.
3. **Pragmatic Portfolio Design**: We deliberately avoid over-engineering patterns such as full CQRS with MediatR, event sourcing, or microservices. For this application's scale, straightforward service-based application orchestration ensures that code is easy to debug, performant, readable, and defensible in technical interviews.
4. **Incremental Feature Slices**: The API project is established as the composition root at the very beginning (Phase 1) with basic middleware, health checks, OpenAPI/Swagger, and DI wiring. Endpoints and services are added incrementally across phases rather than deferring the web API until late in the project.

---

## 2. Project Structure & Dependency Directions

The solution is structured into four core application projects and two test projects:

```text
InsuranceClaimsTracker/
│
├── src/
│   ├── InsuranceClaimsTracker.Domain/          # Enterprise Business Rules & Entities
│   ├── InsuranceClaimsTracker.Application/     # Application Use Cases, DTOs & Contracts
│   ├── InsuranceClaimsTracker.Infrastructure/  # EF Core, Database Migrations & External APIs
│   └── InsuranceClaimsTracker.Api/             # Composition Root, ASP.NET Core Web API, Controllers
│
├── tests/
│   ├── InsuranceClaimsTracker.UnitTests/       # Domain Logic & Application Service Unit Tests
│   └── InsuranceClaimsTracker.IntegrationTests/# WebApplicationFactory, Integration & Concurrency Tests
│
├── frontend/                                   # React + TypeScript + Vite + Tailwind CSS
│
└── docs/                                       # Project Documentation & Architecture Plans
```

### Explicit Dependency Direction Rules

1. **`InsuranceClaimsTracker.Domain`**:
   - **Dependencies**: **None**.
   - Contains pure C# types: entities, value objects, domain enums, domain exceptions, and entity invariants.
   - Has zero references to EF Core, ASP.NET Core, or other application projects.
2. **`InsuranceClaimsTracker.Application`**:
   - **Dependencies**: Depends **only on `Domain`**.
   - Contains use case services, DTOs, interfaces (`IApplicationDbContext`, `IJwtTokenGenerator`, `IPasswordHasher`, `IClaimAiAdvisoryService`), and FluentValidation validators.
3. **`InsuranceClaimsTracker.Infrastructure`**:
   - **Dependencies**: Implements **`Application`** abstractions and uses **`Domain`**.
   - Contains EF Core `ApplicationDbContext`, entity configurations, migrations, password hasher implementation (PBKDF2), JWT token generator, and LLM external API client.
4. **`InsuranceClaimsTracker.Api`**:
   - **Dependencies**: Acts as the **Composition Root**; references **`Application`** and **`Infrastructure`**.
   - Registers dependency injection containers, configures middleware pipeline, hosts ASP.NET Core controllers, configures authentication handlers, and sets up OpenAPI/Swagger.

### Dependency Flow Diagram

```mermaid
flowchart TD
    API["InsuranceClaimsTracker.Api\n(Composition Root, Controllers, DI, Middleware)"]
    INFRA["InsuranceClaimsTracker.Infrastructure\n(EF Core, DbContext, Migrations, External Clients)"]
    APP["InsuranceClaimsTracker.Application\n(Use Cases, DTOs, Service Interfaces, Validators)"]
    DOM["InsuranceClaimsTracker.Domain\n(Entities, Enums, Invariants, Domain Exceptions)"]

    API -->|Wires DI & routes| APP
    API -->|Wires DI & DB| INFRA
    INFRA -->|Implements abstractions| APP
    INFRA -->|Maps entities| DOM
    APP -->|Coordinates business models| DOM
```

---

## 3. Project Responsibilities Breakdown

### `InsuranceClaimsTracker.Domain`
- **Purpose**: Represents the core state and invariants of the domain.
- **Dependencies**: Pure C# standard library.
- **Contents**:
  - **Entities**: `User`, `Policy`, `Claim`, `ClaimHistory`.
  - **Enums**: `UserRole` (`Customer = 1`, `Admin = 2`), `PolicyType` (`Health = 1`, `Auto = 2`, `Home = 3`, `Life = 4`), `ClaimStatus` (`Submitted = 1`, `UnderReview = 2`, `Approved = 3`, `Rejected = 4`, `Paid = 5`).
  - **Domain Exceptions**: `InvalidClaimStateException`, `PolicyExpiredException`, `CoverageExceededException`, `UnauthorizedResourceAccessException`.
  - **Domain Rules**: Validation of state transitions and claim eligibility.

### `InsuranceClaimsTracker.Application`
- **Purpose**: Orchestrates application use cases and business operations.
- **Dependencies**: `Domain`, validation libraries (`FluentValidation`).
- **Contents**:
  - **Interfaces**:
    - Data Access: `IApplicationDbContext`.
    - Identity & Security: `IJwtTokenGenerator`, `IPasswordHasher`, `ICurrentUserContext`.
    - External Services: `IClaimAiAdvisoryService`.
  - **Services**:
    - `AuthService`: Registration (strictly locked to Customer role), credential verification, JWT issuance.
    - `PolicyService`: Policy queries with strict ownership enforcement, admin policy issuance.
    - `ClaimService`: Claim submission, eligibility verification, customer claim retrieval.
    - `ClaimReviewService`: Admin claim processing (review, approve, reject, pay), concurrency conflict handling, and append-only audit trail logging.
  - **DTOs**: Request and Response models.
  - **Validators**: FluentValidation rules.

### `InsuranceClaimsTracker.Infrastructure`
- **Purpose**: Handles all technical concerns, database interactions, and external integrations.
- **Dependencies**: `Application`, `Domain`, `Microsoft.EntityFrameworkCore.SqlServer`.
- **Contents**:
  - **Data Access**:
    - `ApplicationDbContext`: Implements `IApplicationDbContext`.
    - `Configurations/`: Entity type configurations (`IEntityTypeConfiguration<T>`) defining keys, columns, relationships, cascade rules, `RowVersion`, and check constraints.
    - `Migrations/`: Version-controlled EF Core database migrations.
  - **Security Implementations**:
    - `PasswordHasher`: PBKDF2 with HMAC-SHA512.
    - `JwtTokenGenerator`: Issues signed HS256 JWT tokens.
    - `CurrentUserContext`: Extracts `UserId` and `Role` from the HTTP request's `ClaimsPrincipal`.
  - **External Integrations**:
    - `ClaimAiAdvisoryService`: Structured prompt LLM client for advisory categorization and triage summaries.

### `InsuranceClaimsTracker.Api`
- **Purpose**: Composition root and HTTP entry point.
- **Dependencies**: `Application`, `Infrastructure`.
- **Contents**:
  - **Composition & Startup**:
    - `Program.cs`: Wires up DI services from `Application` and `Infrastructure`, configures health checks (`/health`), OpenAPI/Swagger with JWT support, CORS, and rate limiting.
  - **Controllers**:
    - `AuthController`: Registration and login endpoints.
    - `PoliciesController`: Policy management endpoints.
    - `ClaimsController`: Customer claim submission and query endpoints.
    - `AdminClaimsController`: Admin review, decision, payout, and AI triage endpoints.
  - **Middleware**:
    - `ExceptionHandlingMiddleware`: Translates domain exceptions into RFC 7807 `ProblemDetails` JSON responses, including `409 Conflict` for concurrency errors.
    - `RequestLoggingMiddleware`: Logs HTTP operations and timing.

---

## 4. Architectural Decision Records (ADRs) & Interview Defense

### Decision 1: Why Establish the API Project in Phase 1?
- **Rationale**: Deferring the API layer until late in the development cycle creates a "big bang" integration where routing, DI registration, configuration, and JSON serialization bugs surface simultaneously. Initializing `InsuranceClaimsTracker.Api` as a buildable project with `/health` and Swagger in Phase 1 establishes the composition root early, allowing each phase to deliver vertical slices with working endpoints and tests.

### Decision 2: Why Service-Based Layered Architecture instead of MediatR/CQRS?
- **Rationale**: For an application with ~10 distinct use cases, MediatR adds unnecessary layers of reflection, pipeline abstractions, and debugging friction without providing real architectural benefits. A clean, interface-driven service layer provides identical testability and decoupling with clearer stack traces and straightforward dependency injection.

### Decision 3: How is Resource Ownership Enforced against BOLA / IDOR?
- **Rationale**: Role-based access control (`[Authorize(Roles = "Customer")]`) only validates that the caller is a customer, **not** that they own the requested resource. The `ClaimService` and `PolicyService` explicitly query records scoped by `CustomerId`:
  ```csharp
  var claim = await _context.Claims
      .Include(c => c.Policy)
      .FirstOrDefaultAsync(c => c.Id == claimId && c.Policy.CustomerId == currentUserId);
  ```
  If not found or not owned, the API returns `404 Not Found`, completely mitigating **BOLA (Broken Object Level Authorization)** / IDOR vulnerabilities.

### Decision 4: Why Must Registration Strictly Forbid Self-Assignment of the Admin Role?
- **Rationale**: Allowing callers to pass a `Role` property during public registration is a severe privilege escalation vulnerability. The `RegisterRequestDto` contains only `Name`, `Email`, and `Password`. The backend unconditionally assigns `UserRole.Customer`. Admin accounts can only be provisioned via database seed scripts or protected administrative workflows.

### Decision 5: Why is ClaimHistory Append-Only?
- **Rationale**: In insurance and financial systems, audit trails are legal records. The application exposes zero `UPDATE` or `DELETE` endpoints for `ClaimHistory`. Every claim status change creates a new record containing the previous status, new status, actor ID, timestamp, and review note.

### Decision 6: Why Place GenAI Integration After Baseline Testing?
- **Rationale**: Core claim lifecycle rules, financial state transitions, and authorization policies form the critical foundation of the system. GenAI is purely advisory and non-deterministic. Implementing and verifying the deterministic workflows and test suites first ensures the core domain is robust before adding external AI integrations.

---

## 5. Related Documentation Links
- [Database Schema & State Transitions](DATABASE_SCHEMA.md)
- [API Contract & Endpoint Specifications](API_CONTRACT.md)
- [Security Architecture & Deployment Guide](SECURITY_AND_DEPLOYMENT.md)
- [Phased Implementation Plan](IMPLEMENTATION_PLAN.md)
