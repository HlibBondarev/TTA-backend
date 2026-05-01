-- =============================================================
-- AUTHENTICATION & AUTHORIZATION STORED FUNCTIONS & PROCEDURES
-- =============================================================
/**********************************************************************************
 * Retrieves the effective user role for a specific target or global scope.
 * Returns an INTEGER mapping to C# AppRole (0: FullControl, 1: Editor, 2: Viewer).
 * Logic: Checks direct policies first, then derives team-level access
 **********************************************************************************/
CREATE OR REPLACE FUNCTION auth.get_user_permission(
    p_user_id VARCHAR(64),
    p_target_type INT, -- 0: Global, 1: Club, 2: Team
    p_target_id UUID DEFAULT NULL
)
RETURNS INT AS $$
DECLARE
    v_role INT;
    v_club_id UUID;
BEGIN
    -- 1. Check direct top-level policies (Global or Direct Club level)
    -- Logic: Returns the most permissive role if a Global or direct Club policy exists.
    SELECT role INTO v_role
    FROM auth.accesspolicies 
    WHERE userid = p_user_id 
      AND (
          (targettype = 0) -- Global level
          OR 
          (targettype = 1 AND p_target_type = 1 AND targetid = p_target_id) -- Direct Club level
      )
      AND (expiresat IS NULL OR expiresat > CURRENT_TIMESTAMP)
    ORDER BY role ASC
    LIMIT 1;

    -- 2. If target is a Team, check inheritance and direct team policies in auth.accesspolicies
    IF v_role IS NULL AND p_target_type = 2 THEN
        -- Resolve parent club to check for inherited club-level permissions
        SELECT clubid INTO v_club_id FROM public.teams WHERE id = p_target_id;

        SELECT role INTO v_role
        FROM (
            -- a) Inherited from parent Club policy (targettype = 1)
            SELECT role FROM auth.accesspolicies 
            WHERE userid = p_user_id AND targettype = 1 AND targetid = v_club_id
              AND (expiresat IS NULL OR expiresat > CURRENT_TIMESTAMP)
            
            UNION ALL

            -- b) Direct Team Policy (targettype = 2) 
            -- This is the record managed strictly by the TerminateMemberHandler
            SELECT role FROM auth.accesspolicies 
            WHERE userid = p_user_id AND targettype = 2 AND targetid = p_target_id
              AND (expiresat IS NULL OR expiresat > CURRENT_TIMESTAMP)
        ) AS combined_roles
        ORDER BY role ASC LIMIT 1;
    END IF;

    RETURN v_role;
END;$$ LANGUAGE plpgsql;

/**********************************************************************
 * Universal upsert for access policies. 
 * To revoke access, we call this with p_expiresat = CURRENT_TIMESTAMP.
 **********************************************************************/
CREATE OR REPLACE FUNCTION auth.upsert_access_policy(
    p_id UUID,
    p_userid VARCHAR(64),
    p_targettype INT,
    p_targetid UUID,
    p_role INT,
    p_createdat TIMESTAMPTZ,
    p_expiresat TIMESTAMPTZ DEFAULT NULL
)
RETURNS SETOF auth.accesspolicies AS $$
BEGIN
    RETURN QUERY
    INSERT INTO auth.accesspolicies (id, userid, targettype, targetid, role, createdat, expiresat)
    VALUES (p_id, p_userid, p_targettype, p_targetid, p_role, p_createdat, p_expiresat)
    ON CONFLICT (id) DO UPDATE SET
        role = EXCLUDED.role,
        expiresat = EXCLUDED.expiresat
    RETURNING *;
END;$$ LANGUAGE plpgsql;

/*******************************************************************************
 * Retrieves an active access policy for a specific user within a specific team.
 * An active policy is one where expiresat is either NULL or in the future.
 *******************************************************************************/
CREATE OR REPLACE FUNCTION auth.get_active_team_policy(
    p_user_id VARCHAR(64),
    p_team_id UUID
)
RETURNS SETOF auth.accesspolicies AS $$
BEGIN
    RETURN QUERY
    SELECT * FROM auth.accesspolicies
    WHERE userid = p_user_id
      AND targettype = 2 -- Team scope
      AND targetid = p_team_id
      AND (expiresat IS NULL OR expiresat > CURRENT_TIMESTAMP);
END;$$ LANGUAGE plpgsql;

-- ==========================================
-- GEOGRAPHY & USERS
-- ==========================================
/*******************************
 * Retrieve a single user by ID
 *******************************/
CREATE OR REPLACE FUNCTION public.get_user_by_id(p_id TEXT)
RETURNS SETOF public.users AS $$BEGIN
    RETURN QUERY
    SELECT * FROM public.users 
    WHERE id = p_id;
END;$$ LANGUAGE plpgsql;

/********************************************
 * Retrieves all users with a specific email.
 ********************************************/
CREATE OR REPLACE FUNCTION public.get_users_by_email(p_email TEXT)
RETURNS SETOF public.users AS $$
BEGIN
    RETURN QUERY
    SELECT * FROM public.users 
    WHERE email = p_email;
END;
$$ LANGUAGE plpgsql;

-- ====================================================
-- ORGANIZATIONS (CLUBS) STORED FUNCTIONS & PROCEDURES
-- ====================================================
/******************************************************************************
 * Atomic function to ensure user existence and create a club with ownership.
 * Maintains strict 'one club per owner' rule via unique constraint validation.
 ******************************************************************************/
CREATE OR REPLACE FUNCTION auth.create_club_with_ownership(
    p_id UUID,
    p_cityid UUID,
    p_name TEXT,
    p_ownerid TEXT,
    p_owner_email TEXT,    -- Added for JIT registration
    p_owner_name TEXT,     -- Added for JIT registration
    p_createdat TIMESTAMPTZ
) RETURNS UUID AS $$
DECLARE
    v_constraint_name TEXT;
BEGIN
    -- 1. Just-in-Time User Registration
    INSERT INTO public.users (id, email, displayname, createdat)
    VALUES (p_ownerid, p_owner_email, p_owner_name, p_createdat)
    ON CONFLICT (id) DO NOTHING;

    -- 2. Insert the club record
    INSERT INTO public.clubs (id, cityid, name, createdat)
    VALUES (p_id, p_cityid, p_name, p_createdat);

    -- 3. Insert the ownership policy
    -- targettype 1: Club, role 0: FullControl
    INSERT INTO auth.accesspolicies (userid, targettype, targetid, role, createdat)
    VALUES (p_ownerid, 1, p_id, 0, p_createdat);

    RETURN p_id;

EXCEPTION 
    WHEN unique_violation THEN
        GET STACKED DIAGNOSTICS v_constraint_name = CONSTRAINT_NAME;
        IF v_constraint_name = 'uix_accesspolicies_club_owner' THEN
            RAISE EXCEPTION 'User already owns a club.' USING ERRCODE = '23505';
        ELSE
            RAISE;
        END IF;
END;
$$ LANGUAGE plpgsql;

/******************************************************************************
 * Checks if a user already owns any club to enforce "one club per user" rule.
 ******************************************************************************/
CREATE OR REPLACE FUNCTION auth.check_user_owns_any_club(
    p_user_id TEXT
)
RETURNS BOOLEAN AS $$BEGIN
    RETURN EXISTS (
        SELECT 1 
        FROM auth.accesspolicies 
        WHERE userid = p_user_id 
          AND targettype = 1 -- 1: Club
          AND role = 0       -- 0: FullControl
          AND expiresat IS NULL
    );
END;$$ LANGUAGE plpgsql;

-- ====================================================
-- TEAM MANAGEMENT STORED FUNCTIONS & PROCEDURES
-- ====================================================
/********************************************************
 * Upserts a team record and returns the updated entity.
 ********************************************************/
CREATE OR REPLACE FUNCTION public.upsert_team(
    p_id UUID,
    p_clubid UUID,
    p_sportid UUID,
    p_name VARCHAR(100),
    p_minbirthyear INT,
    p_gender INT, -- 0 for Male, 1 for Female
    p_createdat TIMESTAMPTZ
)
RETURNS SETOF public.teams AS $$
BEGIN
    -- 1. Validate gender input
    IF p_gender NOT IN (0, 1) THEN
        RAISE EXCEPTION 'Invalid gender value: %. Expected 0 (Male) or 1 (Female).', p_gender 
        USING ERRCODE = '22023';
    END IF;

    RETURN QUERY
    INSERT INTO public.teams (id, clubid, sportid, name, minbirthyear, gender, createdat)
    VALUES (p_id, p_clubid, p_sportid, p_name, p_minbirthyear, p_gender, p_createdat)
    ON CONFLICT (id) DO UPDATE SET
        clubid = EXCLUDED.clubid,
        sportid = EXCLUDED.sportid,
        name = EXCLUDED.name,
        minbirthyear = EXCLUDED.minbirthyear,
        gender = EXCLUDED.gender
    RETURNING *;
END;
$$ LANGUAGE plpgsql;

/*******************************************************
 * Retrieves all teams associated with a specific club.
 *******************************************************/
CREATE OR REPLACE FUNCTION public.get_teams_by_club(p_club_id UUID)
RETURNS SETOF public.teams AS $$
BEGIN
    RETURN QUERY
    SELECT * FROM public.teams 
    WHERE clubid = p_club_id
    ORDER BY name ASC;
END;
$$ LANGUAGE plpgsql;

/*******************************
 * Retrieve a single team by ID
 *******************************/
CREATE OR REPLACE FUNCTION public.get_team_by_id(p_id UUID)
RETURNS SETOF public.teams AS $$BEGIN
    RETURN QUERY
    SELECT * FROM public.teams WHERE id = p_id;
END;$$ LANGUAGE plpgsql;

-- ===============================================================================
-- TEAM MEMBERSHIP STORED FUNCTIONS
-- ===============================================================================
/*********************************************************************************
 * Enforce Business Rule 1 at the DB level to prevent concurrent-insert races.
 * This ensures only one active role of a specific type exists per user in a team.
 *********************************************************************************/
CREATE UNIQUE INDEX IF NOT EXISTS uix_teammemberships_active_role_per_team 
ON public.teammemberships (teamid, userid, roleinteam) 
WHERE (leftat IS NULL);

/**********************************************************************************************
 * Upsert function for team memberships with integrated access policy management.
 * Enforces strict integrity rules to maintain consistent team roles and permissions.
 * This is the single source of truth for all membership changes, including terminations.
 * Business Rules Enforced:
 * a. Prevents duplicate active roles for the same user in a team.
 * b. Ensures only one membership is marked as 'isprimary' for the user across all teams.
 * c. Uses p_approle to maintain a matching record in auth.accesspolicies for standard lookups.
 **********************************************************************************************/
CREATE OR REPLACE FUNCTION public.upsert_team_membership_with_policy(
    p_id UUID,
    p_teamid UUID,
    p_userid VARCHAR(64),
    p_roleinteam INT,
    p_isprimary BOOLEAN,
    p_joinedat TIMESTAMPTZ,
    p_approle INT -- Now used to synchronize the security policy
)
RETURNS SETOF public.teammemberships AS $$
BEGIN
    -- Business Rule 1: Prevent duplicate active roles in the same team
    IF EXISTS (
        SELECT 1 FROM public.teammemberships 
        WHERE teamid = p_teamid 
          AND userid = p_userid 
          AND roleinteam = p_roleinteam 
          AND leftat IS NULL
          AND id <> p_id 
    ) THEN
        RAISE EXCEPTION 'User already has an active membership with role % in this team.', p_roleinteam
        USING ERRCODE = '23505';
    END IF;

    -- Business Rule 2: Reset other primary flags for active memberships
    IF p_isprimary THEN
        UPDATE public.teammemberships
        SET isprimary = FALSE
        WHERE userid = p_userid 
            AND isprimary = TRUE 
            AND leftat IS NULL
            AND id <> p_id;
    END IF;

    -- 1. Insert or Update the membership record
    INSERT INTO public.teammemberships (id, teamid, userid, roleinteam, isprimary, joinedat)
    VALUES (p_id, p_teamid, p_userid, p_roleinteam, p_isprimary, p_joinedat)
    ON CONFLICT (id) DO UPDATE 
    SET 
        teamid = EXCLUDED.teamid,
        userid = EXCLUDED.userid,
        roleinteam = EXCLUDED.roleinteam,
        isprimary = EXCLUDED.isprimary,
        joinedat = EXCLUDED.joinedat;

    -- 2. Synchronize the security policy in the auth schema
    -- This ensures p_approle is used (Rabbit's fix) and permissions are explicitly stored.
    INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat)
    VALUES (gen_random_uuid(), p_userid, p_approle, 2, p_teamid, NOW())
    ON CONFLICT (userid, targettype, targetid) DO UPDATE 
    SET role = EXCLUDED.role;

    RETURN QUERY SELECT * FROM public.teammemberships WHERE id = p_id;
END;$$ LANGUAGE plpgsql;

/*****************************************************************
 * Retrieves all active members of a specific team in json-format.
 *****************************************************************/
CREATE OR REPLACE FUNCTION public.get_team_members_json(p_team_id UUID)
RETURNS TEXT AS $$
BEGIN
    RETURN (
        SELECT COALESCE(json_agg(t), '[]'::json)::TEXT
        FROM (
            SELECT 
                m.id AS "MembershipId",
                u.id AS "UserId",
                u.displayname AS "DisplayName",
                u.email AS "Email",
                m.roleinteam AS "RoleInTeam",
                m.joinedat AS "JoinedAt",
                m.isprimary AS "IsPrimary"
            FROM public.teammemberships m
            JOIN public.users u ON m.userid = u.id
            WHERE m.teamid = p_team_id AND m.leftat IS NULL
            ORDER BY m.joinedat DESC
        ) t
    );
END;
$$ LANGUAGE plpgsql;

/***************************************************************
 * Returns all active memberships for a user in a specific team.
 * A user can have multiple roles (e.g., Player and Captain).
 ***************************************************************/
CREATE OR REPLACE FUNCTION public.get_active_memberships_by_email(
    p_team_id UUID,
    p_email VARCHAR(255)
)
RETURNS SETOF public.teammemberships AS $$
BEGIN
    RETURN QUERY
    SELECT m.* FROM public.teammemberships m
    JOIN public.users u ON m.userid = u.id
    WHERE m.teamid = p_team_id 
      AND u.email = p_email 
      AND m.leftat IS NULL;
END;$$ LANGUAGE plpgsql;

/**************************************************************************
 * Returns a specific active membership for a user in a team by their role.
 **************************************************************************/
CREATE OR REPLACE FUNCTION public.get_active_membership_by_email_and_role(
    p_team_id UUID,
    p_email VARCHAR(255),
    p_role INT
)
RETURNS SETOF public.teammemberships AS $$
BEGIN
    RETURN QUERY
    SELECT m.* FROM public.teammemberships m
    JOIN public.users u ON m.userid = u.id
    WHERE m.teamid = p_team_id 
      AND u.email = p_email 
      AND m.roleinteam = p_role
      AND m.leftat IS NULL;
END;$$ LANGUAGE plpgsql;

/********************************************************************************************
 * Universal upsert for memberships (handles creation, updating and termination via p_leftat)
 ********************************************************************************************/
CREATE OR REPLACE FUNCTION public.upsert_team_membership(
    p_id UUID,
    p_userid VARCHAR(64),
    p_teamid UUID,
    p_roleinteam INT,
    p_joinedat TIMESTAMPTZ,
    p_isprimary BOOLEAN,
    p_leftat TIMESTAMPTZ DEFAULT NULL
)
RETURNS SETOF public.teammemberships AS $$
BEGIN
    RETURN QUERY
    INSERT INTO public.teammemberships (id, userid, teamid, roleinteam, joinedat, isprimary, leftat)
    VALUES (p_id, p_userid, p_teamid, p_roleinteam, p_joinedat, p_isprimary, p_leftat)
    ON CONFLICT (id) DO UPDATE SET
        roleinteam = EXCLUDED.roleinteam,
        isprimary = EXCLUDED.isprimary,
        leftat = EXCLUDED.leftat
    RETURNING *;
END;$$ LANGUAGE plpgsql;

-- ====================================================
-- PLAYERS STORED FUNCTIONS & PROCEDURES
-- ====================================================
/*********************************
 * Retrieve a single player by ID
 *********************************/
CREATE OR REPLACE FUNCTION public.get_player_by_id(p_id UUID)
RETURNS SETOF public.players AS $$BEGIN
    RETURN QUERY
    SELECT * FROM public.players WHERE id = p_id;
END;$$ LANGUAGE plpgsql;

/*******************************************************************************************
 * Upsert function for players: inserts a new player or updates existing one based on ID.
 *******************************************************************************************/
CREATE OR REPLACE FUNCTION public.upsert_player(
    p_id UUID,
    p_homeclubid UUID,
    p_firstname VARCHAR(100),
    p_lastname VARCHAR(100),
    p_birthdate DATE,
    p_gender INT,
    p_createdat TIMESTAMPTZ
)
RETURNS SETOF public.players AS $$
BEGIN
    -- Validate gender input
    IF p_gender NOT IN (0, 1) THEN
        RAISE EXCEPTION 'Invalid gender value: %. Expected 0 (Male) or 1 (Female).', p_gender 
        USING ERRCODE = '22023';
    END IF;

    RETURN QUERY
    INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat)
    VALUES (p_id, p_homeclubid, p_firstname, p_lastname, p_birthdate, p_gender, p_createdat)
    ON CONFLICT (id) DO UPDATE SET
        homeclubid = EXCLUDED.homeclubid,
        firstname = EXCLUDED.firstname,
        lastname = EXCLUDED.lastname,
        birthdate = EXCLUDED.birthdate,
        gender = EXCLUDED.gender
    RETURNING *;
END;$$ LANGUAGE plpgsql;

/*********************************************************
 * Retrieves all players associated with a specific club.
 *********************************************************/

CREATE OR REPLACE FUNCTION public.get_players_by_club(p_club_id UUID)
RETURNS SETOF public.players AS $$BEGIN
    RETURN QUERY
    SELECT * FROM public.players 
    WHERE homeclubid = p_club_id;
END;$$ LANGUAGE plpgsql;

-- =============================================================
-- TOURNAMENT MANAGEMENT FUNCTIONS
-- =============================================================
/*************************************************************************
 * Inserts a new tournament or updates an existing one based on its ID.
 * Validates that the start date precedes the end date.
 * Check existence of related entities (Foreign Keys).
 * Authorization: Owner check for updates
 *************************************************************************/
CREATE OR REPLACE FUNCTION public.upsert_tournament(
    p_id UUID,
    p_sportid UUID,
    p_configurationid UUID,
    p_cityid UUID,
    p_ownerid VARCHAR(64),
    p_name VARCHAR(100),
    p_startdate TIMESTAMPTZ,
    p_enddate TIMESTAMPTZ,
    p_createdat TIMESTAMPTZ
)
RETURNS SETOF public.tournaments AS $$
BEGIN
    -- 1. Validation: Check existence of related entities (Foreign Keys)
    -- Explicitly raising 23503 allows the C# handler to catch it as a NotFoundException
    IF NOT EXISTS (SELECT 1 FROM public.cities WHERE id = p_cityid) THEN
        RAISE EXCEPTION 'City with id % not found', p_cityid USING ERRCODE = '23503';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.sports WHERE id = p_sportid) THEN
        RAISE EXCEPTION 'Sport with id % not found', p_sportid USING ERRCODE = '23503';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.sportconfigurations WHERE id = p_configurationid) THEN
        RAISE EXCEPTION 'Sport configuration with id % not found', p_configurationid USING ERRCODE = '23503';
    END IF;

    -- 2. Validation: Date logic
    IF p_enddate IS NOT NULL AND p_startdate >= p_enddate THEN
        RAISE EXCEPTION 'Invalid tournament dates: StartDate (%) must be earlier than EndDate (%).', 
            p_startdate, p_enddate 
        USING ERRCODE = '22023'; -- Invalid Parameter Value
    END IF;

    -- 3. Authorization: Owner check for updates
    IF EXISTS (SELECT 1 FROM public.tournaments WHERE id = p_id) THEN
        IF NOT EXISTS (SELECT 1 FROM public.tournaments WHERE id = p_id AND ownerid = p_ownerid) THEN
            RAISE EXCEPTION 'Access denied: You are not the owner of this tournament.'
            USING ERRCODE = 'P0001';
        END IF;
    END IF;

    -- 4. Execution: Upsert operation
    RETURN QUERY
    INSERT INTO public.tournaments (
        id, 
        sportid, 
        configurationid, 
        cityid, 
        ownerid, 
        name, 
        startdate, 
        enddate, 
        createdat
    )
    VALUES (
        p_id, 
        p_sportid, 
        p_configurationid, 
        p_cityid, 
        p_ownerid, 
        p_name, 
        p_startdate, 
        p_enddate, 
        p_createdat
    )
    ON CONFLICT (id) DO UPDATE SET
        sportid = EXCLUDED.sportid,
        configurationid = EXCLUDED.configurationid,
        cityid = EXCLUDED.cityid,
        name = EXCLUDED.name,
        startdate = EXCLUDED.startdate,
        enddate = EXCLUDED.enddate
        -- ownerid and createdat are preserved (not updated)
    RETURNING *;
END;
$$ LANGUAGE plpgsql;

/**********************************************************
 * Retrieves a single tournament by its unique identifier.
 **********************************************************/
CREATE OR REPLACE FUNCTION public.get_tournament_by_id(p_id UUID)
RETURNS SETOF public.tournaments AS $$
BEGIN
    RETURN QUERY
    SELECT * FROM public.tournaments WHERE id = p_id;
END;
$$ LANGUAGE plpgsql;

-- =============================================================
-- ROSTER MANAGEMENT FUNCTIONS
-- =============================================================
/*******************************************************************************************
 * Adds or updates a player's assignment in a specific tournament roster.
 * Includes a safety check to prevent cross-team player movement within the same tournament.
 *******************************************************************************************/
CREATE OR REPLACE FUNCTION public.upsert_player_to_roster(
    p_id UUID,
    p_tournament_id UUID,
    p_team_id UUID,
    p_player_id UUID,
    p_position_id UUID,
    p_number INT,
    p_created_at TIMESTAMPTZ
)
RETURNS SETOF public.playerrosters AS $$
BEGIN
    -- Validation: Check if the player is already registered for a DIFFERENT team in this tournament.
    -- This prevents silent reassignment and allows the C# Handler to catch SQLSTATE P0001.
    IF EXISTS (
        SELECT 1 FROM public.playerrosters 
        WHERE tournamentid = p_tournament_id 
          AND playerid = p_player_id
          AND teamid != p_team_id
    ) THEN
        RAISE EXCEPTION 'Player is already registered for another team in this tournament.' 
        USING ERRCODE = 'P0001';
    END IF;

    RETURN QUERY
    INSERT INTO public.playerrosters (
        id, tournamentid, teamid, playerid, positionid, number, createdat
    )
    VALUES (
        p_id, p_tournament_id, p_team_id, p_player_id, p_position_id, p_number, p_created_at
    )
    -- Handle existing registration for the SAME team (tournamentid, playerid conflict).
    ON CONFLICT (tournamentid, playerid) 
    DO UPDATE SET 
        positionid = EXCLUDED.positionid,
        number = EXCLUDED.number,
        createdat = EXCLUDED.createdat
        -- teamid is not updated as the IF EXISTS check above ensures it remains the same.
    RETURNING *;
END;
$$ LANGUAGE plpgsql;

/******************************************************************
 * Retrieves the full roster for a specific team in a tournament.
 * Joins with players and position definitions for a complete view.
 ******************************************************************/
CREATE OR REPLACE FUNCTION public.get_tournament_team_roster(
    p_tournament_id UUID,
    p_team_id UUID
)
RETURNS TABLE (
    id UUID,
    tournamentid UUID,
    teamid UUID,
    playerid UUID,
    firstname VARCHAR,
    lastname VARCHAR,
    positionid UUID,
    positionname VARCHAR,
    number INT,
    createdat TIMESTAMPTZ
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        r.id,
        r.tournamentid,
        r.teamid,
        r.playerid,
        p.firstname,
        p.lastname,
        r.positionid,
        pos.name as positionname,
        r.number,
        r.createdat
    FROM public.playerrosters r
    INNER JOIN public.players p ON r.playerid = p.id
    INNER JOIN public.playerpositiondefinitions pos ON r.positionid = pos.id
    WHERE r.tournamentid = p_tournament_id 
      AND r.teamid = p_team_id
    ORDER BY r.number ASC;
END;
$$ LANGUAGE plpgsql;

/********************************************
 * Removes a player from a tournament roster.
 ********************************************/
CREATE OR REPLACE FUNCTION public.remove_player_from_roster(
    p_tournament_id UUID,
    p_team_id UUID,
    p_player_id UUID
)
RETURNS VOID AS $$
BEGIN
    DELETE FROM public.playerrosters 
    WHERE 
        tournamentid = p_tournament_id 
        AND teamid = p_team_id
        AND playerid = p_player_id;
END;
$$ LANGUAGE plpgsql;

-- =============================================================
-- MATCH MANAGEMENT FUNCTIONS
-- =============================================================
/***************************************************************************************************
 * Upserts a match record with referential integrity checks.
 * Validates tournament existence and ensures both teams are registered in the tournament's rosters.
 ***************************************************************************************************/
CREATE OR REPLACE FUNCTION public.upsert_match(
    p_id UUID,
    p_tournament_id UUID,
    p_home_team_id UUID,
    p_guest_team_id UUID,
    p_scheduled_at TIMESTAMPTZ,
    p_match_number VARCHAR,
    p_venue VARCHAR,
    p_temperature FLOAT,
    p_home_score INT,
    p_guest_score INT,
    p_created_at TIMESTAMPTZ
)
RETURNS SETOF public.matches AS $$
BEGIN
    -- 1. Validate Tournament existence
    IF NOT EXISTS (SELECT 1 FROM public.tournaments WHERE id = p_tournament_id) THEN
        RAISE EXCEPTION 'Tournament with ID % not found.', p_tournament_id USING ERRCODE = 'P0002';
    END IF;

    -- 2. Validate that both teams are participants of the tournament (check playerrosters)
    IF NOT EXISTS (SELECT 1 FROM public.playerrosters WHERE tournamentid = p_tournament_id AND teamid = p_home_team_id) THEN
        RAISE EXCEPTION 'Home team % is not registered for this tournament.', p_home_team_id USING ERRCODE = 'P0001';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.playerrosters WHERE tournamentid = p_tournament_id AND teamid = p_guest_team_id) THEN
        RAISE EXCEPTION 'Guest team % is not registered for this tournament.', p_guest_team_id USING ERRCODE = 'P0001';
    END IF;

    RETURN QUERY
    INSERT INTO public.matches (
        id, tournamentid, hometeamid, guestteamid, scheduledat, 
        matchnumber, venue, temperature, homescore, guestscore, createdat
    )
    VALUES (
        p_id, p_tournament_id, p_home_team_id, p_guest_team_id, p_scheduled_at, 
        p_match_number, p_venue, p_temperature, p_home_score, p_guest_score, p_created_at
    )
    ON CONFLICT (id) DO UPDATE SET
        tournamentid = EXCLUDED.tournamentid,
        hometeamid = EXCLUDED.hometeamid,
        guestteamid = EXCLUDED.guestteamid,
        scheduledat = EXCLUDED.scheduledat,
        matchnumber = EXCLUDED.matchnumber,
        venue = EXCLUDED.venue,
        temperature = EXCLUDED.temperature,
        homescore = EXCLUDED.homescore,
        guestscore = EXCLUDED.guestscore
    RETURNING *;
END;
$$ LANGUAGE plpgsql;

/********************************************************************************************
 * Retrieves all matches for a tournament with joined metadata (tournament name, team names).
 ********************************************************************************************/
CREATE OR REPLACE FUNCTION public.get_tournament_matches(p_tournament_id UUID)
RETURNS TABLE (
    id UUID,
    tournamentid UUID,
    tournamentname VARCHAR,
    hometeamid UUID,
    hometeamname VARCHAR,
    guestteamid UUID,
    guestteamname VARCHAR,
    scheduledat TIMESTAMPTZ,
    matchnumber VARCHAR,
    venue VARCHAR,
    temperature DOUBLE PRECISION,
    homescore INT,
    guestscore INT,
    createdat TIMESTAMPTZ
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        m.id, m.tournamentid, t.name as tournamentname,
        m.hometeamid, ht.name as hometeamname,
        m.guestteamid, gt.name as guestteamname,
        m.scheduledat, m.matchnumber, m.venue, m.temperature, m.homescore, m.guestscore, m.createdat
    FROM public.matches m
    INNER JOIN public.tournaments t ON m.tournamentid = t.id
    INNER JOIN public.teams ht ON m.hometeamid = ht.id
    INNER JOIN public.teams gt ON m.guestteamid = gt.id
    WHERE m.tournamentid = p_tournament_id
    ORDER BY m.scheduledat ASC;
END;
$$ LANGUAGE plpgsql;

/************************************************************************************************
 * Retrieves a single match by its unique identifier.
 * Returns only columns defined in the public.matches table to match the Match entity structure.
 ************************************************************************************************/
CREATE OR REPLACE FUNCTION public.get_match_by_id(p_id UUID)
RETURNS SETOF public.matches AS $$
BEGIN
    RETURN QUERY
    SELECT m.*
    FROM public.matches m
    WHERE m.id = p_id;
END;
$$ LANGUAGE plpgsql;

/************************************************************************************************
 * Retrieves a single match by its unique identifier with joined metadata (Team and Tournament names).
 ************************************************************************************************/
CREATE OR REPLACE FUNCTION public.get_match_with_details_by_id(p_id UUID)
RETURNS TABLE (
    id UUID,
    tournamentid UUID,
    tournamentname VARCHAR,
    hometeamid UUID,
    hometeamname VARCHAR,
    guestteamid UUID,
    guestteamname VARCHAR,
    scheduledat TIMESTAMPTZ,
    matchnumber VARCHAR,
    venue VARCHAR,
    temperature DOUBLE PRECISION,
    homescore INT,
    guestscore INT,
    createdat TIMESTAMPTZ
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        m.id, m.tournamentid, t.name as tournamentname,
        m.hometeamid, ht.name as hometeamname,
        m.guestteamid, gt.name as guestteamname,
        m.scheduledat, m.matchnumber, m.venue, m.temperature, m.homescore, m.guestscore, m.createdat
    FROM public.matches m
    INNER JOIN public.tournaments t ON m.tournamentid = t.id
    INNER JOIN public.teams ht ON m.hometeamid = ht.id
    INNER JOIN public.teams gt ON m.guestteamid = gt.id
    WHERE m.id = p_id;
END;
$$ LANGUAGE plpgsql;

-- =============================================================
-- MATCHLINEUP MANAGEMENT FUNCTIONS
-- =============================================================
/**********************************************************************************
 * Upserts a player into the match lineup with full business rule validation.
 * * Validations:
 * 1. SQLSTATE 'P0001': Ensures player belongs to one of the teams in the match.
 * 2. SQLSTATE 'P0003': Validates team lineup limit for the match configuration.
 * 3. SQLSTATE '23505': Managed by database unique constraint on (matchid, playerrosterid).
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.upsert_match_lineup(
    p_id UUID,
    p_matchid UUID,
    p_playerrosterid UUID,
    p_number INT,
    p_isinstartinglineup BOOLEAN,
    p_positionid UUID
)
RETURNS SETOF public.matchlineups AS $$
DECLARE
    v_sport_id UUID;
    v_lineup_limit INT;
    v_current_count INT;
    v_team_id UUID;
    v_final_id UUID := COALESCE(p_id, gen_random_uuid());
BEGIN
    -- 1. Resolve the team ID from the tournament roster
    SELECT teamid INTO v_team_id FROM public.playerrosters WHERE id = p_playerrosterid;

    -- 2. Validation: Ensure the player belongs to either the Home or Guest team of the match
    IF NOT EXISTS (
        SELECT 1 FROM public.matches 
        WHERE id = p_matchid AND (hometeamid = v_team_id OR guestteamid = v_team_id)
    ) THEN
        RAISE EXCEPTION 'Player does not belong to any team participating in this match.' USING ERRCODE = 'P0001';
    END IF;

    -- 3. Validation: Check lineup limit (only for new entries)
    -- We check if the ID exists. If not, it's a new entry that might exceed the limit.
    IF NOT EXISTS (SELECT 1 FROM public.matchlineups WHERE id = v_final_id) THEN
        SELECT t.sportid, sc.lineuplimit INTO v_sport_id, v_lineup_limit
        FROM public.matches m
        JOIN public.tournaments t ON m.tournamentid = t.id
        JOIN public.sportconfigurations sc ON t.configurationid = sc.id
        WHERE m.id = p_matchid;

        SELECT COUNT(*) INTO v_current_count 
        FROM public.matchlineups ml
        JOIN public.playerrosters pr ON ml.playerrosterid = pr.id
        WHERE ml.matchid = p_matchid AND pr.teamid = v_team_id;

        IF v_current_count >= v_lineup_limit THEN
            RAISE EXCEPTION 'Team lineup limit exceeded for Match %.', p_matchid USING ERRCODE = 'P0003';
        END IF;
    END IF;

    -- 4. Atomic Upsert using ON CONFLICT
    RETURN QUERY
    INSERT INTO public.matchlineups (
        id, matchid, playerrosterid, number, isinstartinglineup, positionid
    )
    VALUES (
        v_final_id, p_matchid, p_playerrosterid, p_number, p_isinstartinglineup, p_positionid
    )
    ON CONFLICT (id) DO UPDATE SET
        number = EXCLUDED.number,
        isinstartinglineup = EXCLUDED.isinstartinglineup,
        positionid = EXCLUDED.positionid
    RETURNING *;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Retrieves the full match lineup with player and position details.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.get_match_lineup(p_matchid UUID)
RETURNS TABLE (
    id UUID,
    matchid UUID,
    teamid UUID,
    playerrosterid UUID,
    firstname VARCHAR,
    lastname VARCHAR,
    number INT,
    isinstartinglineup BOOLEAN,
    positionid UUID,
    positionname VARCHAR
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        ml.id, ml.matchid, pr.teamid, ml.playerrosterid,
        p.firstname, p.lastname, ml.number, ml.isinstartinglineup,
        ml.positionid, ppd.name
    FROM public.matchlineups ml
    JOIN public.playerrosters pr ON ml.playerrosterid = pr.id
    JOIN public.players p ON pr.playerid = p.id
    JOIN public.playerpositiondefinitions ppd ON ml.positionid = ppd.id
    WHERE ml.matchid = p_matchid
    ORDER BY pr.teamid, ml.number;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Removes a player from the match lineup.
 * Returns the number of rows affected (1 if deleted, 0 if not found).
 * Due to ON DELETE CASCADE settings, related records in playerpresences 
 * are removed automatically.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.delete_match_lineup_item(p_id UUID)
RETURNS INT AS $$
DECLARE
    v_affected_count INT;
BEGIN
    DELETE FROM public.matchlineups
    WHERE id = p_id;
    
    GET DIAGNOSTICS v_affected_count = ROW_COUNT;
    RETURN v_affected_count;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Bulk copies all players from a team's tournament roster to a match protocol.
 * Initialized with roster defaults: jersey number, position, and starting flag = FALSE.
 * Uses ON CONFLICT to skip players already present in the match lineup.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.copy_team_roster_to_match_lineup(
    p_matchid UUID,
    p_teamid UUID
)
RETURNS INT AS $$
DECLARE
    v_inserted_count INT;
    v_tournamentid UUID;
    v_lineup_limit INT;
    v_current_count INT;
    v_roster_count INT;
BEGIN
    -- 1. Resolve tournament ID and verify team participation
    SELECT tournamentid INTO v_tournamentid 
    FROM public.matches 
    WHERE id = p_matchid AND (hometeamid = p_teamid OR guestteamid = p_teamid);

    IF v_tournamentid IS NULL THEN
        RAISE EXCEPTION 'Team % does not belong to match % or match not found.', p_teamid, p_matchid 
        USING ERRCODE = 'P0001';
    END IF;

    -- 2. Get lineup limit for the match
    SELECT sc.lineuplimit INTO v_lineup_limit
    FROM public.matches m
    JOIN public.tournaments t ON m.tournamentid = t.id
    JOIN public.sportconfigurations sc ON t.configurationid = sc.id
    WHERE m.id = p_matchid;

    -- 3. Calculate how many players we want to add vs how many we can
    SELECT COUNT(*) INTO v_roster_count
    FROM public.playerrosters pr
    WHERE pr.teamid = p_teamid AND pr.tournamentid = v_tournamentid
    AND NOT EXISTS ( -- Only count players not already in the lineup
        SELECT 1 FROM public.matchlineups ml 
        WHERE ml.matchid = p_matchid AND ml.playerrosterid = pr.id
    );

    SELECT COUNT(*) INTO v_current_count
    FROM public.matchlineups ml
    JOIN public.playerrosters pr ON ml.playerrosterid = pr.id
    WHERE ml.matchid = p_matchid AND pr.teamid = p_teamid;

    IF (v_current_count + v_roster_count) > v_lineup_limit THEN
        RAISE EXCEPTION 'Adding % players would exceed the lineup limit (%) for this team.', v_roster_count, v_lineup_limit
        USING ERRCODE = 'P0003';
    END IF;

    -- 4. Perform efficient bulk insert
    INSERT INTO public.matchlineups (
        id, matchid, playerrosterid, number, isinstartinglineup, positionid
    )
    SELECT 
        gen_random_uuid(), p_matchid, pr.id, pr.number, FALSE, pr.positionid 
    FROM public.playerrosters pr
    WHERE pr.teamid = p_teamid AND pr.tournamentid = v_tournamentid
    ON CONFLICT (matchid, playerrosterid) DO NOTHING;

    GET DIAGNOSTICS v_inserted_count = ROW_COUNT;
    RETURN v_inserted_count;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Retrieves a single match lineup record by its unique identifier.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.get_match_lineup_by_id(p_id UUID)
RETURNS SETOF public.matchlineups AS $$
BEGIN
    RETURN QUERY
    SELECT * FROM public.matchlineups WHERE id = p_id;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Retrieves a single match lineup record with joined player and position details.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.get_match_lineup_details_by_id(p_id UUID)
RETURNS TABLE (
    id UUID,
    matchid UUID,
    teamid UUID,
    playerrosterid UUID,
    firstname VARCHAR,
    lastname VARCHAR,
    number INT,
    isinstartinglineup BOOLEAN,
    positionid UUID,
    positionname VARCHAR
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        ml.id, ml.matchid, pr.teamid, ml.playerrosterid,
        p.firstname, p.lastname, ml.number, ml.isinstartinglineup,
        ml.positionid, ppd.name
    FROM public.matchlineups ml
    JOIN public.playerrosters pr ON ml.playerrosterid = pr.id
    JOIN public.players p ON pr.playerid = p.id
    JOIN public.playerpositiondefinitions ppd ON ml.positionid = ppd.id
    WHERE ml.id = p_id;
END;$$ LANGUAGE plpgsql;
