CREATE SCHEMA IF NOT EXISTS auth;

-- ==========================================
-- 1. GEOGRAPHY & USERS
-- ==========================================

CREATE TABLE countries (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE,
    code VARCHAR(3) NOT NULL UNIQUE,
    createdat TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE regions (
    id SERIAL PRIMARY KEY,
    countryid INT NOT NULL REFERENCES countries(id) ON DELETE RESTRICT,
    name VARCHAR(100) NOT NULL,
    UNIQUE(countryid, name)
);

CREATE INDEX ix_regions_countryid ON regions (countryid);

CREATE TABLE cities (
    id UUID PRIMARY KEY,
    regionid INT NOT NULL REFERENCES regions(id),
    name VARCHAR(100) NOT NULL,
    UNIQUE(regionid, name)
);

CREATE TABLE users (
    id VARCHAR(64) PRIMARY KEY,
    email VARCHAR(255) NOT NULL UNIQUE,
    displayname VARCHAR(50) NOT NULL CHECK (char_length(displayname) >= 3),
    createdat TIMESTAMPTZ NOT NULL
);

-- ==========================================
-- 2. SPORT DEFINITIONS
-- ==========================================

CREATE TABLE sports (
    id UUID PRIMARY KEY,
    name VARCHAR(50) NOT NULL UNIQUE,
    defaultconfigid UUID NULL 
);

CREATE TABLE playerpositiondefinitions (
    id UUID PRIMARY KEY,
    sportid UUID NOT NULL REFERENCES sports(id) ON DELETE CASCADE,
    name VARCHAR(50) NOT NULL,
    shortname VARCHAR(5) NOT NULL
);

CREATE TABLE sportconfigurations (
    id UUID PRIMARY KEY,
    sportid UUID NOT NULL REFERENCES sports(id) ON DELETE CASCADE,
    usescleantime BOOLEAN NOT NULL,
    periodscount INT NOT NULL,
    perioddurationminutes INT NOT NULL,
    fieldsize VARCHAR(50) NOT NULL,
    rosterlimit INT NOT NULL,
    lineuplimit INT NOT NULL,
    UNIQUE (sportid, id)
);

ALTER TABLE sports 
ADD CONSTRAINT fk_sports_default_config 
FOREIGN KEY (id, defaultconfigid) 
REFERENCES sportconfigurations (sportid, id);

-- ==========================================
-- 3. ORGANIZATIONS (CLUBS) & TEAMS
-- ==========================================

CREATE TABLE clubs (
    id UUID PRIMARY KEY,
    cityid UUID NOT NULL REFERENCES public.cities(id),
    name VARCHAR(100) NOT NULL,
    createdat TIMESTAMPTZ NOT NULL
);
CREATE INDEX ix_clubs_cityid ON clubs (cityid);

CREATE TABLE teams (
    id UUID PRIMARY KEY,
    clubid UUID NOT NULL REFERENCES clubs(id) ON DELETE CASCADE,
    sportid UUID NOT NULL REFERENCES sports(id),
    name VARCHAR(100) NOT NULL,
    minbirthyear INT NULL, 
    gender INT NOT NULL, -- 0: Male, 1: Female
    createdat TIMESTAMPTZ NOT NULL,
    CONSTRAINT chk_teams_gender CHECK (gender IN (0, 1))
);

CREATE INDEX ix_teams_clubid ON teams (clubid);
CREATE INDEX ix_teams_sportid ON teams (sportid);
CREATE INDEX ix_teams_gender ON teams (gender);
CREATE INDEX ix_teams_club_sport ON teams (clubid, sportid);

CREATE TABLE teammemberships (
    id UUID PRIMARY KEY,
    userid VARCHAR(64) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    teamid UUID NOT NULL REFERENCES teams(id) ON DELETE CASCADE,
    roleinteam INT NOT NULL, -- Maps to TeamRole Enum
    joinedat TIMESTAMPTZ NOT NULL,
    leftat TIMESTAMPTZ NULL,
    isprimary BOOLEAN NOT NULL,
    CONSTRAINT chk_teammemberships_left_after_joined 
        CHECK (leftat IS NULL OR leftat >= joinedat),
    -- 0:HeadCoach, 1:AssistantCoach, 2:ClubDirector, 3:TeamManager, 4:Analyst, 5:Player, 6:Captain
    CONSTRAINT chk_teammemberships_role CHECK (roleinteam BETWEEN 0 AND 6)
);
-- Create a partial unique index to ensure a user has ONLY ONE active primary membership.
-- This prevents race conditions where a user could end up with multiple isprimary=TRUE 
-- records across different teams.
CREATE UNIQUE INDEX IF NOT EXISTS uix_teammemberships_active_primary_per_user 
ON teammemberships (userid) 
WHERE (isprimary = TRUE AND leftat IS NULL);

-- ==========================================
-- 4. TOURNAMENTS & MATCHES
-- ==========================================

CREATE TABLE public.tournaments (
    id UUID PRIMARY KEY,
    sportid UUID NOT NULL,
    configurationid UUID NOT NULL,
    cityid UUID NOT NULL REFERENCES public.cities(id),
    ownerid VARCHAR(64) NOT NULL REFERENCES public.users(id), -- Added Ownership
    name VARCHAR(200) NOT NULL,
    startdate TIMESTAMPTZ NOT NULL,
    enddate TIMESTAMPTZ NULL,
    createdat TIMESTAMPTZ NOT NULL,
    
    -- Mandatory constraint: Ensures the configuration belongs to the selected sport
    CONSTRAINT fk_tournaments_sport_config
        FOREIGN KEY (sportid, configurationid) 
        REFERENCES public.sportconfigurations (sportid, id),
        
    -- Date integrity check
    CONSTRAINT chk_tournaments_end_after_start 
        CHECK (enddate IS NULL OR enddate >= startdate)
);
-- Essential indexes for performance
CREATE INDEX ix_tournaments_cityid ON public.tournaments (cityid);
CREATE INDEX ix_tournaments_sportid ON public.tournaments (sportid);
CREATE INDEX ix_tournaments_ownerid ON public.tournaments (ownerid);

CREATE TABLE matches (
    id UUID PRIMARY KEY,
    tournamentid UUID NOT NULL REFERENCES tournaments(id) ON DELETE CASCADE,
    hometeamid UUID NOT NULL REFERENCES teams(id),
    guestteamid UUID NOT NULL REFERENCES teams(id),
    scheduledat TIMESTAMPTZ NOT NULL,
    matchnumber VARCHAR(50) NULL,
    venue VARCHAR(200) NULL,
    temperature FLOAT NULL,
    homescore INT NULL,
    guestscore INT NULL,
    createdat TIMESTAMPTZ NOT NULL
);

-- ==========================================
-- 5. PLAYERS & ROSTERS
-- ==========================================

CREATE TABLE players (
    id UUID PRIMARY KEY,
    homeclubid UUID NOT NULL REFERENCES clubs(id),
    firstname VARCHAR(100) NOT NULL,
    lastname VARCHAR(100) NOT NULL,
    birthdate DATE NOT NULL,
    gender INT NOT NULL, -- 0: Male, 1: Female
    createdat TIMESTAMPTZ NOT NULL,
    CONSTRAINT chk_players_gender CHECK (gender IN (0, 1))
);

CREATE TABLE playermetrics (
    id UUID PRIMARY KEY,
    playerid UUID NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    weight FLOAT NULL,
    height FLOAT NULL,
    measuredat TIMESTAMPTZ NOT NULL
);

CREATE TABLE playerrosters (
    id UUID PRIMARY KEY,
    playerid UUID NOT NULL REFERENCES players(id),
    tournamentid UUID NOT NULL REFERENCES tournaments(id) ON DELETE CASCADE,
    teamid UUID NOT NULL REFERENCES teams(id),
    number INT NOT NULL,
    positionid UUID NOT NULL REFERENCES playerpositiondefinitions(id),
    createdat TIMESTAMPTZ NOT NULL,
    
    -- CONSTRAINT 1: Ensures a player is registered ONLY ONCE per tournament (across all teams).
    -- This prevents the same player from representing different teams in one tournament.
    CONSTRAINT uix_playerrosters_tournament_player UNIQUE (tournamentid, playerid),
    
    -- CONSTRAINT 2: Ensures jersey numbers are unique within a single team for a specific tournament.
    -- This handles the race-condition duplicate issue identified by the analysis.
    CONSTRAINT uix_playerrosters_tournament_team_number UNIQUE (tournamentid, teamid, number)
);

-- Indexes to speed up searches
CREATE INDEX ix_playerrosters_tournament ON playerrosters (tournamentid);
CREATE INDEX ix_playerrosters_team ON playerrosters (teamid);

CREATE TABLE matchlineups (
    id UUID PRIMARY KEY,
    matchid UUID NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
    -- Linked to tournament roster instead of general players table
    playerrosterid UUID NOT NULL REFERENCES playerrosters(id) ON DELETE CASCADE,
    number INT NOT NULL, -- Jersey number for this specific match
    isinstartinglineup BOOLEAN NOT NULL DEFAULT false,
    positionid UUID NOT NULL REFERENCES playerpositiondefinitions(id),
    
    -- Ensures a player cannot be added to the same match lineup more than once
    CONSTRAINT uix_matchlineups_match_player UNIQUE (matchid, playerrosterid)
);

-- Index for fast lookup of all players in a specific match
CREATE INDEX ix_matchlineups_matchid ON public.matchlineups (matchid);

-- Index for checking player participation across different matches
CREATE INDEX ix_matchlineups_playerrosterid ON public.matchlineups (playerrosterid);

-- ==========================================
-- 6. TTA ENGINE (EVENTS & TIME)
-- ==========================================

CREATE TABLE eventdefinitions (
    id UUID PRIMARY KEY,
    sportid UUID NOT NULL REFERENCES sports(id) ON DELETE CASCADE,
    name VARCHAR(50) NOT NULL,
    shortname VARCHAR(10) NOT NULL,
    ispositive BOOLEAN NOT NULL,
    createdat TIMESTAMPTZ NOT NULL
);

CREATE TABLE timeanchors (
    id UUID PRIMARY KEY,
    matchid UUID NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
    periodnumber INT NOT NULL,
    type INT NOT NULL, -- Maps to TimeAnchorType Enum
    timestamp TIMESTAMPTZ NOT NULL,
    -- 0:PeriodStart, 1:PeriodEnd, 2:StoppageStart, 3:StoppageEnd
    CONSTRAINT chk_timeanchors_type CHECK (type BETWEEN 0 AND 3)
);

CREATE TABLE gameevents (
    id UUID PRIMARY KEY,
    matchid UUID NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
    -- Linked to match protocol. NULL allowed for team-wide events (e.g., timeouts)
    -- Refactored: Use ON DELETE RESTRICT to prevent losing attribution to events
    matchlineupid UUID NULL REFERENCES matchlineups(id) ON DELETE RESTRICT,
    eventdefinitionid UUID NOT NULL REFERENCES eventdefinitions(id),
    periodnumber INT NOT NULL,
    eventtimestamp TIMESTAMPTZ NOT NULL,
    normalizedmatchtime INTERVAL NULL,
    isleadtogoal BOOLEAN NOT NULL DEFAULT FALSE, 
    createdat TIMESTAMPTZ NOT NULL
);

CREATE TABLE playerpresences (
    id UUID PRIMARY KEY,
    matchid UUID NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
    -- Updated to track match-specific protocol ID
    matchlineupid UUID NOT NULL REFERENCES matchlineups(id) ON DELETE CASCADE,
    periodnumber INT NOT NULL,
    timein TIMESTAMPTZ NOT NULL,
    timeout TIMESTAMPTZ NULL,
    CONSTRAINT chk_playerpresences_timeout_after_timein 
        CHECK (timeout IS NULL OR timeout >= timein)
);

-- Indices for playerpresences to optimize joins and integrity checks
CREATE INDEX ix_playerpresences_matchid ON playerpresences(matchid);
CREATE INDEX ix_playerpresences_matchlineupid ON playerpresences(matchlineupid);

CREATE INDEX ix_gameevents_matchid ON gameevents(matchid);
CREATE INDEX ix_gameevents_matchlineupid ON gameevents(matchlineupid);

CREATE TABLE auth.accesspolicies (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(), 
    userid VARCHAR(64) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role INT NOT NULL, -- 0: FullControl, 1: Editor, 2: Viewer
    targettype INT NOT NULL, -- 0: Global, 1: Club, 2: Team
    targetid UUID NULL,                
    createdat TIMESTAMPTZ NOT NULL,
    expiresat TIMESTAMPTZ NULL,
    
    -- Updated unique constraint to treat multiple NULLs in targetid as the same value.
    -- This ensures a user cannot have duplicate 'Global' policies.
    CONSTRAINT uix_accesspolicies_user_target 
        UNIQUE NULLS NOT DISTINCT (userid, targettype, targetid),

    -- Constraints for data integrity
    CONSTRAINT chk_accesspolicy_role 
        CHECK (role IN (0, 1, 2)),
    CONSTRAINT chk_accesspolicy_targettype 
        CHECK (targettype IN (0, 1, 2)),
        
    -- Logic: Global (0) must have NULL targetid, others (1, 2) must have a value
    CONSTRAINT chk_accesspolicy_targetid_scope_logic CHECK (
        (targettype = 0 AND targetid IS NULL) OR 
        (targettype IN (1, 2) AND targetid IS NOT NULL)
    ),
    CONSTRAINT chk_accesspolicies_expires_after_created 
        CHECK (expiresat IS NULL OR expiresat >= createdat)
);

-- Performance indexes for lookups
CREATE INDEX ix_accesspolicies_userid ON auth.accesspolicies(userid);
CREATE INDEX ix_accesspolicies_scope ON auth.accesspolicies(targettype, targetid);

-- Partial Unique Index for active Club Owners (Business logic constraint)
CREATE UNIQUE INDEX uix_accesspolicies_club_owner 
ON auth.accesspolicies (userid) 
WHERE targettype = 1 -- 1: Club
  AND role = 0 -- 0: FullControl
  AND expiresat IS NULL;