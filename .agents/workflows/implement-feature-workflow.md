---
description: Vertical-slice implementation workflow for a new TTA-backend feature (DB routine -> SqlStatements -> EntityRepository -> MediatR -> Controller -> xUnit tests)
---

Implement the requested `TTA-backend` feature strictly following this bottom-up vertical-slice pipeline and all rules in `.agents/rules/tta-backend.md`:

1. **Database Layer (PostgreSQL Stored Function / Procedure)**:
   - Create or update the idempotent SQL script in `TTA.DataAccess` (`CREATE OR REPLACE FUNCTION` for queries returning data; `CREATE OR REPLACE PROCEDURE` for write commands).
   - Ensure proper schema qualification (`public.<routine_name>`), parameter prefixes (`p_...`), and `GRANT` statements if applicable.

2. **SQL Constants (`SqlStatements` & `SqlParameters`)**:
   - Add the parameterized `SELECT * FROM public.<fn>(...)` or `CALL public.<proc>(...)` query string to `SqlStatements`.
   - Add any new parameter keys to `SqlParameters` (no magic strings).

3. **Data Access Layer (`EntityRepository` -> `BaseRepository`)**:
   - Define the entity/data model if needed.
   - Add the method signature to the entity repository interface (`I<Entity>Repository`) with `CancellationToken` and XML docs.
   - Implement the method in `<Entity>Repository` (inheriting from `BaseRepository`), building `DynamicParameters` (matching the SQL placeholders) and delegating execution to `BaseRepository` with `cancellationToken`.

4. **Business Logic Layer (`TTA.BusinessLogic` — MediatR)**:
   - Create immutable C# `record` types for the `Command` or `Query` and response `DTO`s.
   - Add a `FluentValidation` validator if input validation is required.
   - Implement the `IRequestHandler<TRequest, TResponse>` with primary constructor injection, cognitive complexity < 15, XML documentation, and `CancellationToken` propagation.

5. **Presentation Layer (`TTA.WebAPI` — Thin Controller)**:
   - Add the endpoint action to the appropriate controller (or create a new controller if introducing a new resource).
   - Accept request DTO / route parameters and `CancellationToken`, dispatch via `ISender.Send(..., cancellationToken)`, and return typed HTTP responses (`Ok`, `CreatedAtAction`, `NoContent`, etc.) with XML docs and `[ProducesResponseType]` attributes.

6. **xUnit Tests & Solution Verification**:
   - Write unit tests (xUnit + Moq + FluentAssertions, `Arrange` / `Act` / `Assert`) for the validator, MediatR handler, repository mapping/calls, and controller action.
   - Run `dotnet build TTA-backend.sln` and `dotnet test TTA-backend.sln` from the repository root and resolve all warnings or failures.
