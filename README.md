# 📝 TTA_Notes.txt (Final Architectural Blueprint)
**Project:** TTA-Board (Technical and Tactical Actions)  
**Language:** English (Entities, DB, Code, Data)  
**Core Stack:** .NET 9, Dapper, PostgreSQL (Docker), Auth0  
**UI:** React (Vite) + PWA support  
**Guiding Principle:** Multi-sport flexibility with professional water polo depth
---
### 1. Core Identity & Geography
*   **User:** `Id` (Auth0 string), `Email`, `DisplayName`, `CreatedAt`.
*   **Country:** `Id` (SERIAL), `Name` (Unique), `Code` (e.g., UKR, Unique).
*   **Region:** `Id` (SERIAL), `CountryId`, `Name`. Unique per country.
*   **City:** `Id` (UUID), `RegionId`, `Name`. Unique per region.

### 2. Organizations & Teams
*   **Club:** `Id` (UUID), `CityId`, `Name`, `CreatedAt`.
*   **Team:** `Id`, `ClubId`, `SportId`, `Name`, `MinBirthYear` (nullable), `Gender` (Enum: 0=Male, 1=Female), `CreatedAt`.
*   **TeamMembership:** `Id`, `UserId`, `TeamId`, `RoleInTeam` (Enum), `JoinedAt`, `LeftAt` (nullable), `IsPrimary` (bool).
    *   **TeamRole Enum:** 0:HeadCoach, 1:AssistantCoach, 2:ClubDirector, 3:TeamManager, 4:Analyst, 5:Player, 6:Captain.
    *   **Constraints:** User can have only **one** active `IsPrimary` membership across all teams.

### 3. Sport & Tournament Infrastructure
*   **Sport:** `Id` (UUID), `Name`, `ShortName`, `DefaultConfigId` (FK to configs).
*   **PlayerPositionDefinition:** `Id`, `SportId`, `Name`, `ShortName`.
*   **SportConfiguration:** `Id`, `SportId`, `UsesCleanTime` (bool), `PeriodsCount`, `PeriodDurationMinutes`, `FieldSize`, `RosterLimit`, `LineupLimit`.
*   **Tournament:** `Id`, `SportId`, `ConfigurationId`, `CityId`, `OwnerId` (User FK), `Name`, `StartDate`, `EndDate`, `CreatedAt`.
    *   *Logic:* One user can own a tournament; dates must be sequential.
*   **Match:** `Id`, `TournamentId`, `HomeTeamId`, `GuestTeamId`, `ScheduledAt`, `MatchNumber`, `Venue`, `Temperature`, `HomeScore`, `GuestScore`, `CreatedAt`.
    *   *Integrity:* Both teams **must** have a registered roster in the tournament to create a match.

### 4. Players & Rosters
*   **Player:** `Id`, `HomeClubId`, `FirstName`, `LastName`, `BirthDate`, `Gender` (0:Male, 1:Female), `CreatedAt`.
*   **PlayerMetrics:** `Id`, `PlayerId`, `Weight`, `Height`, `MeasuredAt`.
*   **PlayerRoster (Tournament level):** `Id`, `PlayerId`, `TournamentId`, `TeamId`, `Number`, `PositionId`, `CreatedAt`.
    *   **Constraint 1:** Player registered only **once** per tournament (cannot play for two teams).
    *   **Constraint 2:** Jersey numbers must be unique within a team for a specific tournament.
*   **MatchLineup (Match level):** `Id`, `MatchId`, `PlayerRosterId` (nullable), `Number`, `IsInStartingLineup`, `PositionId` (nullable).

### 5. TTA Engine (Technical & Tactical Actions)
*   **EventDefinition:** `Id`, `SportId`, `Name`, `ShortName`, `IsPositive` (bool), `CreatedAt`.
*   **GameEvent:** `Id`, `MatchLineupId`, `EventDefinitionId`, `PeriodNumber`, `EventTimestamp` (Real Time), `NormalizedMatchTime` (Interval), `IsLeadToGoal` (bool), `CreatedAt`.
*   **PlayerPresence:** `Id`, `MatchLineupId`, `PeriodNumber`, `TimeIn`, `TimeOut`.
*   **TimeAnchor:** `Id`, `MatchId`, `PeriodNumber`, `Type` (Enum: 0:PeriodStart, 1:PeriodEnd, 2:StoppageStart, 3:StoppageEnd), `Timestamp`.

### 6. Architectural Chain & Data Access
*   **Flow:** DB (PostgreSQL) ➔ Functions/Procedures (SQL) ➔ Repository (Dapper) ➔ MediatR Handlers ➔ Controller (API).
*   **Repositories:**
    *   Use `IDbConnection` with Dapper to call stored functions.
    *   **Dynamic Mapping:** Operational and informational lookups (such as match protocols or team lineups) utilize dynamic objects to natively bind joined metadata, eliminating intermediate boilerplate model classes before mapping to final DTOs in handlers. 
    *   **Strongly-Typed Projections:** Complex analytical, calculated, or aggregated data models that do not natively exist inside static database tables (e.g., GameEventProjection, PlayersDirtyTimeByPeriodProjection) use explicitly defined C# records to guarantee compile-time type safety for algorithmic transformations.
    *   **Strict Mapping:** Entity retrieval (e.g., `GetMatchById`) returns only columns matching the base table schema.

### 7. Scoped Access Control Model
*   **Storage:** `auth.accesspolicies` table.
*   **Access Levels (AppRole):** 0:FullControl, 1:Editor, 2:Viewer.
*   **Scopes (TargetType):** 0:Global, 1:Club, 2:Team.
*   **Key Business Rules:**
    *   **One Club Owner:** A user can be `FullControl` owner of only **one** club at a time.
    *   **Inheritance:** Permission for a Club automatically grants access to all its Teams.
    *   **JIT Registration:** `create_club_with_ownership` handles Just-In-Time user creation during club setup.
    *   **Policy Sync:** `upsert_team_membership_with_policy` synchronizes `TeamRole` with `AppRole` in the security table.
    *   **Global policies** utilize targetid = NULL combined with a NULLS NOT DISTINCT constraint to prevent duplicate global assignments for the same user.

### 8. Time Normalization Logic (Piecewise-Linear)
The system calculates "Clean Time" by processing segments between TimeAnchors:
*   **Identify Active Segments:** Intervals between (PeriodStart or StoppageEnd) and (StoppageStart or PeriodEnd).
*   **Calculate Effective Real Duration:** Sum of all active segments' real-world durations.
*   **Coefficient Per Period:** $SportConfiguration.PeriodDurationMinutes / TotalEffectiveRealDuration$.
*   **Normalized Time Formula:** $NormalizedTime = AccumulatedCleanTimeFromPriorSegments + (CurrentEventTimestamp - CurrentSegmentStart) * PeriodCoefficient$.
*   **Player Presence:** Playing time is scaled using the same coefficient, automatically excluding "Dead Time" (Stoppages).

### 9. User Initial Application Workflow (TTA-Board)
*   **Step-1: Sport Discipline Selection:** Select a sport discipline from the full list of available sports in the `sports` table (`GET /api/sports`).
*   **Step-2: Sport Configuration Selection:** Optionally select a specific configuration profile associated with the selected `SportId` from the `sportconfigurations` table (`GET /api/sports/{sportId}/configurations`).
*   **Step-3: Entry Mode Selection (Quick Start vs. Choose Tournament):**
    *   **Quick Start (Active Scope):** Immediately starts a session by executing `POST /api/Matches/quick` with the selected `SportId` and optional `ConfigurationId`. The server provisions Just-In-Time (JIT) match infrastructure (teams, rosters, and lineups) and returns a `QuickMatchResponse` DTO with the match details.
    *   **Choose Tournament (Future Scope):** Allows selecting a pre-existing tournament, match, and team filtered by `SportId` and `ConfigurationId`. *(To be implemented in later phases)*.
*   **Step-4: Team Selection & Action Entry Initialization:** Select which of the two participating teams to track during the match. Calls `GET /api/Matches/{matchId}/teams/{teamId}/lineup` using the selected `matchId` and `teamId`. Successful execution unlocks the TTA input interface for logging player actions.
