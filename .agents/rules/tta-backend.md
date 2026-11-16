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
- **Standard Feature Execution Pipeline**:
  - Follow the vertical execution chain: `Controller Action (TTA.WebAPI)` -> `MediatR Command / Query + Handler + Validator (TTA.BusinessLogic)` -> `Entity Repository (TTA.DataAccess)` -> `SqlStatements` constant -> `EntityRepositoryBase<TKey, TEntity>` -> `PostgreSQL Stored Function / Procedure`.
  - Define all SQL invocation strings in `SqlStatements` constants and parameter names in `SqlParameters` constants.
  - Entity repositories must inherit from `EntityRepositoryBase<TKey, TEntity>` and delegate Dapper execution to `EntityRepositoryBase<TKey, TEntity>` methods rather than opening raw connections directly.
- **Thin Controllers**:
  - Controllers must only accept HTTP requests, validate transport concerns, send MediatR commands/queries via `ISender`, and map results to HTTP status codes (`Ok`, `CreatedAtAction`, `NoContent`, etc.).
  - Never execute business logic, entity orchestration, or direct database queries inside controllers.
- **CancellationToken Propagation**:
  - Propagate `CancellationToken` through all async controller actions, MediatR handlers, Dapper `CommandDefinition` calls, and custom async methods wherever the signature permits it:
    - Controller actions -> `ISender.Send(request, cancellationToken)`
    - MediatR handlers -> `Handle(TRequest request, CancellationToken cancellationToken)`
    - Repository interfaces and implementations -> Dapper calls wrapped in `CommandDefinition(sql, parameters, cancellationToken: cancellationToken)`.
  - Avoid ignoring or dropping cancellation tokens.
  - Framework-mandated async signatures that do not accept a cancellation token (such as `IAsyncDisposable.DisposeAsync()` or `IAsyncLifetime`) are explicitly exempt.
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
  - Parameter names in `DynamicParameters` must match the SQL placeholders used in the query (e.g., `@p_id`), not necessarily the PostgreSQL function/procedure argument names; note that positional routine calls do not require matching PostgreSQL argument names (unlike named notation `arg => @p_id`).
  - Avoid string interpolation or manual concatenation in SQL statements to prevent SQL injection vulnerabilities.
- **PostgreSQL Routine Calls (Functions vs. Procedures)**:
  - Distinguish between PostgreSQL functions and procedures:
    - **Functions** (`CREATE OR REPLACE FUNCTION`): Invoke via parameterized `SELECT` queries (e.g., `SELECT * FROM function_name(@p_param)`) using Dapper `CommandDefinition` and `DynamicParameters`.
    - **Procedures** (`CREATE OR REPLACE PROCEDURE`): Invoke via parameterized `CALL` statements (e.g., `CALL procedure_name(@p_param)`) using Dapper `CommandDefinition` and `DynamicParameters`.
- **Idempotency**:
  - Keep database migrations, schema scripts, and routine definitions idempotent:
    - Use `CREATE OR REPLACE FUNCTION ...` and `CREATE OR REPLACE PROCEDURE ...` for routines.
      - Exception: when a change alters a function's return type or `OUT` parameter types, `CREATE OR REPLACE FUNCTION` fails. Drop dependent objects and then run `DROP FUNCTION IF EXISTS ...` (or run `DROP FUNCTION IF EXISTS ... CASCADE`), followed by `CREATE FUNCTION ...`, and recreate all affected dependent objects and grants in the same migration.
      - When a change alters a function's argument types, `CREATE OR REPLACE FUNCTION` creates a new overload and leaves the old signature in place. Drop the old signature (e.g., `DROP FUNCTION IF EXISTS public.fn_name(integer)`) and its dependent objects, then create the new signature and restore affected dependent objects and grants in the same migration.
      - When a change renames an existing named input parameter, `CREATE OR REPLACE FUNCTION` / `CREATE OR REPLACE PROCEDURE` fails. Drop dependent objects and drop the routine (`DROP FUNCTION IF EXISTS ...` or `DROP PROCEDURE IF EXISTS ...`), then recreate it and restore all affected dependent objects and grants in the same migration.
    - PostgreSQL does not support `CREATE TYPE IF NOT EXISTS`; guard `CREATE TYPE ... AS ENUM` with a catalog check joining `pg_type` and `pg_namespace` filtered by both `t.typname` and the target schema (`n.nspname = 'public'`) inside a `DO $$ ... $$` block, e.g.:
      ```sql
      DO $$ BEGIN IF NOT EXISTS ( SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE t.typname = 'your_enum_type' AND n.nspname = 'public' ) THEN CREATE TYPE public.your_enum_type AS ENUM ('value1', 'value2'); END IF; END $$;
      ```
    - Use `IF NOT EXISTS` for supported statements: `CREATE TABLE`, `ALTER TABLE ... ADD COLUMN`, and `CREATE INDEX`.
      - Exception: when a change alters an existing index's definition, `CREATE INDEX IF NOT EXISTS` keeps the old index unchanged. For standalone indexes, run `DROP INDEX IF EXISTS <index_name>;` followed by `CREATE [UNIQUE] INDEX <index_name> ...` (without `IF NOT EXISTS`) in the same migration. For indexes owned by `UNIQUE` or `PRIMARY KEY` constraints, do not drop the index directly; instead, drop and recreate the owning constraint (`ALTER TABLE ... DROP CONSTRAINT IF EXISTS ...` followed by `ALTER TABLE ... ADD CONSTRAINT ...`) and handle any affected dependencies (such as foreign keys) in the same migration.
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
  - Before concluding any task, run the full build and test suite from the repository root targeting the solution file:
    ```bash
    dotnet build TTA-backend.sln
    dotnet test TTA-backend.sln
    ```
  - Verify and resolve all compilation warnings, analyzer diagnostics, and failing tests before handoff.
- **Git Branching Policy**:
  - Never commit or push directly to `main` or `dev`.
  - Always work in a dedicated, descriptive feature or fix branch (e.g., `feat/<feature-name>`, `fix/<issue-name>`, `chore/<task-name>`).
