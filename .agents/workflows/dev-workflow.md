---
description: End-to-end feature/fix delivery workflow (Issue -> Branch -> Code -> Tests -> Commit -> PR -> Review fixes)
---

Follow this sequential delivery workflow for any new feature, bug fix, or refactoring task:

1. **Draft GitHub Issue (Raw Markdown)**:
   - Before writing code or creating branches, output a clean, ready-to-copy GitHub Issue in a **Raw Markdown** code block (`markdown`) written in English.
   - Include: `Title`, `Summary / Context`, `Acceptance Criteria` (checklist), and `Technical Scope` (affected layers/components and tests).
   - Wait for the user to confirm the Issue (and optionally provide the Issue number) before proceeding.

2. **Create Dedicated Branch**:
   - Ensure local `dev` is up to date, then create and switch to a dedicated branch from `dev` (e.g., `feat/<issue-number>-<short-slug>` or `fix/<issue-number>-<short-slug>`). Never commit directly to `main` or `dev`.

3. **Implement Code**:
   - For `.NET / C#` (`TTA.*`): Follow all repository rules in `.agents/rules/tta-backend.md` (Clean Architecture/CQRS/Dapper, SonarCloud cognitive complexity < 15, English comments/docs).
   - For `Python` (`services/video-analyzer/`): Follow Python best practices, keep cognitive complexity low, and write comments/docstrings strictly in English.

4. **Write & Run Unit Tests**:
   - Run verification for the modified stack(s) (if a PR touches both stacks, run both verification suites):
     - `.NET / C#` (`TTA.*`): Add or update xUnit tests covering all new and modified behaviors, then run `dotnet build TTA-backend.sln` and `dotnet test TTA-backend.sln` from the repository root.
     - `Python` (`services/video-analyzer/`): Add or update unit tests and run `pytest`, `ruff check`, and `ruff format --check` (if configured) inside `services/video-analyzer/`.

5. **Commit Changes**:
   - Stage changes and create a clear Conventional Commit message in English (`feat: ...`, `fix: ...`, `test: ...`) describing what changed and why.

6. **Prepare Pull Request (Raw Markdown)**:
   - After pushing the branch, output a ready-to-copy Pull Request title and description in a **Raw Markdown** code block (linking `Closes #<issue-number>`, summarizing changes, and listing verification steps).

7. **Address SonarCloud & CodeRabbit Review Findings**:
   - When review comments are provided, verify each finding against the actual codebase.
   - Fix only valid issues with minimal, surgical edits; skip false positives or out-of-scope nitpicks with a brief technical justification.
   - Re-run the applicable verification suite(s) from Step 4 before pushing review fixes and draft concise English replies for the reviewer.
