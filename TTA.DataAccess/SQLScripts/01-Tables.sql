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

CREATE TABLE tournaments (
    id UUID PRIMARY KEY,
    sportid UUID NOT NULL REFERENCES sports(id),
    configurationid UUID NOT NULL,
    name VARCHAR(200) NOT NULL,
    startdate TIMESTAMPTZ NOT NULL,
    enddate TIMESTAMPTZ NULL,
    createdat TIMESTAMPTZ NOT NULL,
    CONSTRAINT fk_tournaments_sport_config
        FOREIGN KEY (sportid, configurationid) 
        REFERENCES sportconfigurations (sportid, id),
    CONSTRAINT chk_tournaments_end_after_start 
        CHECK (enddate IS NULL OR enddate >= startdate)
);

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
    positionid UUID NOT NULL REFERENCES playerpositiondefinitions(id)
);

CREATE TABLE matchlineups (
    id UUID PRIMARY KEY,
    matchid UUID NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
    playerid UUID NOT NULL REFERENCES players(id),
    number INT NOT NULL,
    isinstartinglineup BOOLEAN NOT NULL,
    positionid UUID NOT NULL REFERENCES playerpositiondefinitions(id)
);

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
    playerid UUID NULL REFERENCES players(id),
    eventdefinitionid UUID NOT NULL REFERENCES eventdefinitions(id),
    periodnumber INT NOT NULL,
    eventtimestamp TIMESTAMPTZ NOT NULL,
    normalizedmatchtime INTERVAL NULL,
    isleadtogoal BOOLEAN NOT NULL DEFAULT FALSE, 
    createdat TIMESTAMPTZ NOT NULL
);

CREATE INDEX ix_gameevents_matchid ON gameevents(matchid);
CREATE INDEX ix_gameevents_playerid ON gameevents(playerid);

CREATE TABLE playerpresences (
    id UUID PRIMARY KEY,
    matchid UUID NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
    playerid UUID NOT NULL REFERENCES players(id),
    periodnumber INT NOT NULL,
    timein TIMESTAMPTZ NOT NULL,
    timeout TIMESTAMPTZ NULL,
    CONSTRAINT chk_playerpresences_timeout_after_timein 
        CHECK (timeout IS NULL OR timeout >= timein)
);

-- ==========================================
-- 7. ACCESS CONTROL & PERMISSIONS
-- ==========================================

CREATE TABLE auth.accesspolicies (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(), 
    userid VARCHAR(64) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role INT NOT NULL, -- 0: FullControl, 1: Editor, 2: Viewer
    targettype INT NOT NULL, -- 0: Global, 1: Club, 2: Team
    targetid UUID NULL,                
    createdat TIMESTAMPTZ NOT NULL,
    expiresat TIMESTAMPTZ NULL,
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

CREATE INDEX ix_accesspolicies_userid ON auth.accesspolicies(userid);
CREATE INDEX ix_accesspolicies_scope ON auth.accesspolicies(targettype, targetid);

-- Partial Unique Index for active Club Owners
CREATE UNIQUE INDEX uix_accesspolicies_club_owner 
ON auth.accesspolicies (userid) 
WHERE targettype = 1 -- 1: Club
  AND role = 0 -- 0: FullControl
  AND expiresat IS NULL;