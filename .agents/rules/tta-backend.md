---
trigger: always_on
description: "Core engineering standards, Clean Architecture/CQRS guidelines, PostgreSQL/Dapper conventions, SonarCloud quality gates, and verification workflows for TTA-backend."
---

# TTA Backend Engineering Standards

These engineering standards govern all development in the `TTA-backend` repository. All contributors and automated agents must adhere to these rules unconditionally.

---

## 1. Language & Documentation Standards

- **Strict English Requirement**: All code comments, XML documentation comments (`///`), SQL scripts and comments, test names, git commit messages, branch names, and technical documentation must be written strictly in English.
- **XML Documentation**:
  - Document all public classes, interfaces, controller endpoints, MediatR requests/handlers, and repository methods with descriptive XML comments.
  - Include summary, parameter descriptions (`<param>`), and return descriptions (`<returns>`).
  - Document exceptions thrown when relevant (`<exception>`).
- **Git Commit Messages**:
  - Write concise, conventional commit messages in English (e.g., `feat: ...`, `fix: ...`, `refactor: ...`, `test: ...`, `chore: ...`).
  - Do not use non-English characters or slang in commit logs.

---

## 2. Architecture & .NET / C# Standards

- **Clean Architecture & CQRS**:
  - Maintain clear layer boundaries:
    - **Presentation (`TTA.WebAPI`)**: HTTP routing, API authorization policies, content negotiation, model binding.
    - **Application / Business Logic (`TTA.BusinessLogic`)**: MediatR Commands, Queries, Handlers, FluentValidation validators, and pipeline behaviors.
    - **Domain & Infrastructure (`TTA.DataAccess` / `TTA.Common`)**: Repository implementations, Dapper query executions, data models, database connection management, and shared utilities.
- **Thin Controllers**:
  - Controllers must only accept HTTP requests, validate transport concerns, send MediatR commands/queries via `ISender`, and map results to HTTP status codes (`Ok`, `CreatedAtAction`, `NoContent`, etc.).
  - Never execute business logic, entity orchestration, or direct database queries inside controllers.
- **CancellationToken Propagation**:
  - Propagate `CancellationToken` through every asynchronous method signature without exception:
    - Controller actions -> `ISender.Send(request, cancellationToken)`
    - MediatR handlers -> `Handle(TRequest request, CancellationToken cancellationToken)`
    - Repository interfaces and implementations -> Dapper calls wrapped in `CommandDefinition(sql, parameters, cancellationToken: cancellationToken)`.
  - Avoid ignoring or dropping cancellation tokens.
- **DTOs and Encapsulation**:
  - Never expose database entities/models directly from API endpoints.
  - Use explicit DTOs or C# `record` types for all API request bodies and response payloads.
  - Map between database models and API DTOs in MediatR handlers or dedicated mapping profiles.
- **Modern C# Conventions**:
  - Use file-scoped namespaces (`namespace TTA.BusinessLogic.Features.X;`).
  - Use primary constructors where applicable for dependency injection.
  - Prefer immutable records for commands, queries, and DTOs.

---

## 3. PostgreSQL & Dapper Standards

- **Parameterized Queries & DynamicParameters**:
  - Always use parameterized queries or explicit `DynamicParameters` when invoking PostgreSQL routines and queries.
  - Parameter names in `DynamicParameters` must explicitly match PostgreSQL function arguments (e.g., `parameters.Add("p_id", id)`).
  - Avoid string interpolation or manual concatenation in SQL statements to prevent SQL injection vulnerabilities.
- **PostgreSQL Function Calls**:
  - When invoking stored functions or procedures in PostgreSQL (e.g., `public.*`), structure calls using Dapper's `CommandDefinition` and appropriate command types or explicit `SELECT * FROM function_name(@p_param)`.
- **Idempotency**:
  - Keep database migrations, schema scripts, and function definitions idempotent:
    - Use `CREATE OR REPLACE FUNCTION ...` for functions and procedures.
    - Use `IF NOT EXISTS` for table, column, type, or index additions.
    - Write reversible, repeatable migrations and migration scripts.
- **Transaction Boundaries**:
  - Maintain explicit database transactions (`using var transaction = ...`) for multi-step write operations to ensure atomicity.
  - Roll back transactions automatically upon failure or cancellation.

---

## 4. Code Quality & SonarCloud Compliance

- **Low Cognitive Complexity**:
  - Keep methods and MediatR handlers focused and concise.
  - Strictly maintain a cognitive complexity under 15 per method.
  - Break down lengthy logic into small, focused, single-purpose private helper methods or domain services.
- **Extracted Logic & Separation of Concerns**:
  - Extract complex mapping, filtering, calculations, and conditional branching into dedicated, unit-testable methods or domain services.
  - Avoid deep nesting (guard clauses and early returns are preferred).
- **Elimination of Magic Strings & Numbers**:
  - Do not use inline magic strings (e.g., parameter names, policy names, SQL statements) or magic numbers.
  - Group parameter names or SQL queries into static constants (e.g., `SqlStatements`, `SqlParameters`).
  - Use strongly typed enumerations (`enum`) and configuration options (`IOptions<T>`) for application settings.

---

## 5. Testing & Verification Workflow

- **Comprehensive xUnit Test Coverage**:
  - Provide or update unit tests for every new or modified controller action, MediatR command/query handler, validator, and repository method.
  - Write integration tests for database operations or multi-component flows where applicable.
  - Use xUnit, Moq for mocking dependencies, and FluentAssertions for clear, readable assertions.
  - Structure test methods using the `Arrange` / `Act` / `Assert` pattern, naming tests clearly in English (e.g., `MethodName_ShouldExpectedBehavior_WhenStateUnderTest`).
- **Terminal Verification Before Concluding**:
  - Before concluding any task, run the full build and test suite in the terminal:
    ```bash
    dotnet build
    dotnet test
    ```
  - Verify and resolve all compilation warnings, analyzer diagnostics, and failing tests before handoff.
- **Git Branching Policy**:
  - Never commit or push directly to `main` or `dev`.
  - Always work in a dedicated, descriptive feature or fix branch (e.g., `feat/<feature-name>`, `fix/<issue-name>`, `chore/<task-name>`).
