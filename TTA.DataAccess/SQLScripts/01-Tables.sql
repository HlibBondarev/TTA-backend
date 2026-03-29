CREATE SCHEMA IF NOT EXISTS auth;

-- ==========================================
-- 1. GEOGRAPHY & USERS
-- ==========================================

-- table to support multiple countries
CREATE TABLE countries (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE,
    code VARCHAR(3) NOT NULL UNIQUE, -- ISO 3166-1 alpha-3 code
    createdat TIMESTAMP NOT NULL DEFAULT NOW()
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
    id VARCHAR(64) PRIMARY KEY, -- Auth0 Identity ID
    email VARCHAR(255) NOT NULL UNIQUE,
    displayname VARCHAR(20) NOT NULL CHECK (char_length(displayname) >= 3),
    createdat TIMESTAMP NOT NULL
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

-- Enforce that defaultconfigid belongs to the same sport
ALTER TABLE sports 
ADD CONSTRAINT fk_sports_default_config 
FOREIGN KEY (id, defaultconfigid) 
REFERENCES sportconfigurations (sportid, id);

-- ==========================================
-- 3. ORGANIZATIONS & TEAMS
-- ==========================================

CREATE TABLE clubs (
    id UUID PRIMARY KEY,
    cityid UUID NOT NULL REFERENCES cities(id),
    name VARCHAR(100) NOT NULL,
    createdat TIMESTAMP NOT NULL
);

CREATE TABLE teams (
    id UUID PRIMARY KEY,
    clubid UUID NOT NULL REFERENCES clubs(id) ON DELETE CASCADE,
    sportid UUID NOT NULL REFERENCES sports(id),
    name VARCHAR(100) NOT NULL,
    minbirthyear INT NULL, 
    gender VARCHAR(20) NOT NULL, 
    createdat TIMESTAMP NOT NULL,
    CONSTRAINT chk_teams_gender CHECK (gender IN ('Male', 'Female'))
);

CREATE TABLE teammemberships (
    id UUID PRIMARY KEY,
    userid VARCHAR(64) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    teamid UUID NOT NULL REFERENCES teams(id) ON DELETE CASCADE,
    roleinteam VARCHAR(50) NOT NULL,
    joinedat TIMESTAMP NOT NULL,
    leftat TIMESTAMP NULL,
    isprimary BOOLEAN NOT NULL
);

-- ==========================================
-- 4. TOURNAMENTS & MATCHES
-- ==========================================

CREATE TABLE tournaments (
    id UUID PRIMARY KEY,
    sportid UUID NOT NULL REFERENCES sports(id),
    configurationid UUID NOT NULL,
    name VARCHAR(200) NOT NULL,
    startdate TIMESTAMP NOT NULL,
    enddate TIMESTAMP NULL,
    createdat TIMESTAMP NOT NULL,
    CONSTRAINT fk_tournaments_sport_config
    FOREIGN KEY (sportid, configurationid) 
    REFERENCES sportconfigurations (sportid, id)
);

CREATE TABLE matches (
    id UUID PRIMARY KEY,
    tournamentid UUID NOT NULL REFERENCES tournaments(id) ON DELETE CASCADE,
    hometeamid UUID NOT NULL REFERENCES teams(id),
    guestteamid UUID NOT NULL REFERENCES teams(id),
    scheduledat TIMESTAMP NOT NULL,
    matchnumber VARCHAR(50) NULL,
    venue VARCHAR(200) NULL,
    temperature FLOAT NULL,
    homescore INT NULL,
    guestscore INT NULL,
    createdat TIMESTAMP NOT NULL
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
    gender VARCHAR(20) NOT NULL, 
    createdat TIMESTAMP NOT NULL
);

CREATE TABLE playermetrics (
    id UUID PRIMARY KEY,
    playerid UUID NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    weight FLOAT NULL,
    height FLOAT NULL,
    measuredat TIMESTAMP NOT NULL
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
    createdat TIMESTAMP NOT NULL
);

CREATE TABLE timeanchors (
    id UUID PRIMARY KEY,
    matchid UUID NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
    periodnumber INT NOT NULL,
    type VARCHAR(50) NOT NULL, 
    timestamp TIMESTAMP NOT NULL
);

CREATE TABLE gameevents (
    id UUID PRIMARY KEY,
    matchid UUID NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
    playerid UUID NULL REFERENCES players(id),
    eventdefinitionid UUID NOT NULL REFERENCES eventdefinitions(id),
    periodnumber INT NOT NULL,
    eventtimestamp TIMESTAMP NOT NULL, 
    normalizedmatchtime INTERVAL NULL,
    isleadtogoal BOOLEAN NOT NULL DEFAULT FALSE, 
    createdat TIMESTAMP NOT NULL
);

CREATE INDEX ix_gameevents_matchid ON gameevents(matchid);
CREATE INDEX ix_gameevents_playerid ON gameevents(playerid);

CREATE TABLE playerpresences (
    id UUID PRIMARY KEY,
    matchid UUID NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
    playerid UUID NOT NULL REFERENCES players(id),
    periodnumber INT NOT NULL,
    timein TIMESTAMP NOT NULL,
    timeout TIMESTAMP NULL
);

-- ==========================================
-- 7. ACCESS CONTROL & PERMISSIONS
-- ==========================================

CREATE TABLE auth.accesspolicies (
    id UUID PRIMARY KEY,
    userid VARCHAR(64) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role VARCHAR(20) NOT NULL, 
    targettype VARCHAR(20) NOT NULL, 
    targetid UUID NULL,               
    createdat TIMESTAMP NOT NULL,
    expiresat TIMESTAMP NULL,

    CONSTRAINT chk_accesspolicy_role CHECK (role IN ('FullControl', 'Editor', 'Viewer')),
    CONSTRAINT chk_accesspolicy_targettype CHECK (targettype IN ('Global', 'Club', 'Team')),

    CONSTRAINT chk_accesspolicy_targetid_scope_logic CHECK (
        (targettype = 'Global' AND targetid IS NULL) OR 
        (targettype IN ('Club', 'Team') AND targetid IS NOT NULL)
    ),

    CONSTRAINT chk_accesspolicy_dates CHECK (expiresat IS NULL OR expiresat > createdat)
);

CREATE INDEX ix_accesspolicies_userid ON auth.accesspolicies(userid);
CREATE INDEX ix_accesspolicies_scope ON auth.accesspolicies(targettype, targetid);