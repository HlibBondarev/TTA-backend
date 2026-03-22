-- ==========================================
-- 1. GEOGRAPHY & USERS
-- ==========================================

CREATE TABLE Regions (
    Id SERIAL PRIMARY KEY,
    Name VARCHAR(100) NOT NULL UNIQUE
);

CREATE TABLE Cities (
    Id UUID PRIMARY KEY,
    RegionId INT NOT NULL REFERENCES Regions(Id),
    Name VARCHAR(100) NOT NULL,
    UNIQUE(RegionId, Name)
);

CREATE TABLE Users (
    Id VARCHAR(64) PRIMARY KEY, -- Auth0 Identity ID
    Email VARCHAR(255) NOT NULL UNIQUE,
    DisplayName VARCHAR(20) NOT NULL CHECK (char_length(DisplayName) >= 3),
    CreatedAt TIMESTAMP NOT NULL
);

-- ==========================================
-- 2. ORGANIZATIONS & TEAMS
-- ==========================================

CREATE TABLE Clubs (
    Id UUID PRIMARY KEY,
    CityId UUID NOT NULL REFERENCES Cities(Id),
    Name VARCHAR(100) NOT NULL,
    CreatedAt TIMESTAMP NOT NULL
);

CREATE TABLE Teams (
    Id UUID PRIMARY KEY,
    ClubId UUID NOT NULL REFERENCES Clubs(Id) ON DELETE CASCADE,
    Name VARCHAR(100) NOT NULL,
    CreatedAt TIMESTAMP NOT NULL
);

CREATE TABLE TeamMemberships (
    Id UUID PRIMARY KEY,
    UserId VARCHAR(64) NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
    TeamId UUID NOT NULL REFERENCES Teams(Id) ON DELETE CASCADE,
    RoleInTeam VARCHAR(50) NOT NULL, -- Enum: HeadCoach, Player, etc.
    JoinedAt TIMESTAMP NOT NULL,
    LeftAt TIMESTAMP NULL,
    IsPrimary BOOLEAN NOT NULL
);

-- ==========================================
-- 3. SPORT & TOURNAMENTS
-- ==========================================

CREATE TABLE Sports (
    Id UUID PRIMARY KEY,
    Name VARCHAR(50) NOT NULL UNIQUE,
    DefaultConfigId UUID NULL 
);

CREATE TABLE PlayerPositionDefinitions (
    Id UUID PRIMARY KEY,
    SportId UUID NOT NULL REFERENCES Sports(Id) ON DELETE CASCADE,
    Name VARCHAR(50) NOT NULL,
    ShortName VARCHAR(5) NOT NULL
);

CREATE TABLE SportConfigurations (
    Id UUID PRIMARY KEY,
    SportId UUID NOT NULL REFERENCES Sports(Id) ON DELETE CASCADE,
    UsesCleanTime BOOLEAN NOT NULL,
    PeriodsCount INT NOT NULL,
    PeriodDurationMinutes INT NOT NULL,
    FieldSize VARCHAR(50) NOT NULL,
    RosterLimit INT NOT NULL,
    LineupLimit INT NOT NULL
);

ALTER TABLE Sports ADD CONSTRAINT fk_default_config FOREIGN KEY (DefaultConfigId) REFERENCES SportConfigurations(Id);

CREATE TABLE Tournaments (
    Id UUID PRIMARY KEY,
    SportId UUID NOT NULL REFERENCES Sports(Id),
    ConfigurationId UUID NOT NULL REFERENCES SportConfigurations(Id),
    Name VARCHAR(200) NOT NULL,
    StartDate TIMESTAMP NOT NULL,
    EndDate TIMESTAMP NULL,
    CreatedAt TIMESTAMP NOT NULL
);

CREATE TABLE Matches (
    Id UUID PRIMARY KEY,
    TournamentId UUID NOT NULL REFERENCES Tournaments(Id) ON DELETE CASCADE,
    HomeTeamId UUID NOT NULL REFERENCES Teams(Id),
    GuestTeamId UUID NOT NULL REFERENCES Teams(Id),
    ScheduledAt TIMESTAMP NOT NULL,
    MatchNumber VARCHAR(50) NULL,
    Venue VARCHAR(200) NULL,
    Temperature FLOAT NULL,
    HomeScore INT NULL,
    GuestScore INT NULL,
    CreatedAt TIMESTAMP NOT NULL
);

-- ==========================================
-- 4. PLAYERS & ROSTERS
-- ==========================================

CREATE TABLE Players (
    Id UUID PRIMARY KEY,
    HomeClubId UUID NOT NULL REFERENCES Clubs(Id),
    FirstName VARCHAR(100) NOT NULL,
    LastName VARCHAR(100) NOT NULL,
    BirthDate DATE NOT NULL,
    Gender VARCHAR(20) NOT NULL, 
    CreatedAt TIMESTAMP NOT NULL
);

CREATE TABLE PlayerMetrics (
    Id UUID PRIMARY KEY,
    PlayerId UUID NOT NULL REFERENCES Players(Id) ON DELETE CASCADE,
    Weight FLOAT NULL,
    Height FLOAT NULL,
    MeasuredAt TIMESTAMP NOT NULL
);

CREATE TABLE PlayerRosters (
    Id UUID PRIMARY KEY,
    PlayerId UUID NOT NULL REFERENCES Players(Id),
    TournamentId UUID NOT NULL REFERENCES Tournaments(Id) ON DELETE CASCADE,
    TeamId UUID NOT NULL REFERENCES Teams(Id),
    Number INT NOT NULL,
    PositionId UUID NOT NULL REFERENCES PlayerPositionDefinitions(Id)
);

CREATE TABLE MatchLineups (
    Id UUID PRIMARY KEY,
    MatchId UUID NOT NULL REFERENCES Matches(Id) ON DELETE CASCADE,
    PlayerId UUID NOT NULL REFERENCES Players(Id),
    Number INT NOT NULL,
    IsInStartingLineup BOOLEAN NOT NULL,
    PositionId UUID NOT NULL REFERENCES PlayerPositionDefinitions(Id)
);

-- ==========================================
-- 5. TTA ENGINE (EVENTS & TIME)
-- ==========================================

CREATE TABLE EventDefinitions (
    Id UUID PRIMARY KEY,
    SportId UUID NOT NULL REFERENCES Sports(Id) ON DELETE CASCADE,
    Name VARCHAR(50) NOT NULL,
    ShortName VARCHAR(10) NOT NULL, -- Used for mobile UI buttons
    IsPositive BOOLEAN NOT NULL,
    CreatedAt TIMESTAMP NOT NULL
);

CREATE TABLE TimeAnchors (
    Id UUID PRIMARY KEY,
    MatchId UUID NOT NULL REFERENCES Matches(Id) ON DELETE CASCADE,
    PeriodNumber INT NOT NULL,
    Type VARCHAR(50) NOT NULL, 
    Timestamp TIMESTAMP NOT NULL
);

CREATE TABLE GameEvents (
    Id UUID PRIMARY KEY,
    MatchId UUID NOT NULL REFERENCES Matches(Id) ON DELETE CASCADE,
    PlayerId UUID NULL REFERENCES Players(Id),
    EventDefinitionId UUID NOT NULL REFERENCES EventDefinitions(Id),
    PeriodNumber INT NOT NULL,
    EventTimestamp TIMESTAMP NOT NULL, 
    NormalizedMatchTime INTERVAL NULL,
    -- Indicates if the action led to a goal
    IsLeadToGoal BOOLEAN NOT NULL DEFAULT FALSE, 
    CreatedAt TIMESTAMP NOT NULL
);

-- Indexing for optimized lookups by match and player
CREATE INDEX IX_GameEvents_MatchId ON GameEvents(MatchId);
CREATE INDEX IX_GameEvents_PlayerId ON GameEvents(PlayerId);

CREATE TABLE PlayerPresences (
    Id UUID PRIMARY KEY,
    MatchId UUID NOT NULL REFERENCES Matches(Id) ON DELETE CASCADE,
    PlayerId UUID NOT NULL REFERENCES Players(Id),
    PeriodNumber INT NOT NULL,
    TimeIn TIMESTAMP NOT NULL,
    TimeOut TIMESTAMP NULL
);

-- ==========================================
-- 6. ACCESS CONTROL & PERMISSIONS
-- ==========================================

CREATE TABLE AccessPolicies (
    Id UUID PRIMARY KEY,
    UserId VARCHAR(64) NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,   
    -- Storing Enum names for readability: 'FullControl', 'Editor', 'Viewer'
    Role VARCHAR(20) NOT NULL, 
    -- Scope definition: 'Global', 'Club', 'Team'
    TargetType VARCHAR(20) NOT NULL, 
    TargetId UUID NULL,             -- NULL for Global scope
    CreatedAt TIMESTAMP NOT NULL,
    ExpiresAt TIMESTAMP NULL        
);

CREATE INDEX IX_AccessPolicies_UserId ON AccessPolicies(UserId);
CREATE INDEX IX_AccessPolicies_Scope ON AccessPolicies(TargetType, TargetId);