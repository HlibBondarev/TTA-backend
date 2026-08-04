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

/**********************************************************************************
 * Removes an access policy record from the auth.accesspolicies table by identifier.
 * Returns TRUE if a record was actually deleted, FALSE otherwise.
 * Used for compensating transactions and entity cleanups.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION auth.delete_access_policy(
    p_id UUID
)
RETURNS BOOLEAN AS $$
BEGIN
    DELETE FROM auth.accesspolicies
    WHERE id = p_id;

    RETURN FOUND;
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

/********************************************************************************
 * Upserts a user record into the public.users table.
 * Used for Just-In-Time (JIT) user registration upon authentication/quick actions.
 ********************************************************************************/
CREATE OR REPLACE FUNCTION public.upsert_user(
    p_id VARCHAR(64),
    p_email VARCHAR(255),
    p_displayname VARCHAR(50),
    p_createdat TIMESTAMPTZ
)
RETURNS SETOF public.users AS $$
BEGIN
    RETURN QUERY
    INSERT INTO public.users (id, email, displayname, createdat)
    VALUES (p_id, p_email, p_displayname, p_createdat)
    ON CONFLICT (id) DO UPDATE SET
        email = EXCLUDED.email,
        displayname = EXCLUDED.displayname
    RETURNING *;
END;$$ LANGUAGE plpgsql;

/********************************************************************************
 * Removes a user record from the public.users table by identifier.
 * Returns TRUE if a record was actually deleted, FALSE otherwise.
 * Used for compensating transactions and entity cleanups.
 ********************************************************************************/
CREATE OR REPLACE FUNCTION public.delete_user(
    p_id VARCHAR(64)
)
RETURNS BOOLEAN AS $$
BEGIN
    DELETE FROM public.users
    WHERE id = p_id;

    RETURN FOUND;
END;$$ LANGUAGE plpgsql;

-- =============================================================
-- SPORT & SPORT CONFIGURATION STORED FUNCTIONS
-- =============================================================
/*********************************
 * Retrieve a single sport by ID
 *********************************/
CREATE OR REPLACE FUNCTION public.get_sport_by_id(p_id UUID)
RETURNS SETOF public.sports AS $$
BEGIN
    RETURN QUERY
    SELECT * FROM public.sports WHERE id = p_id;
END;$$ LANGUAGE plpgsql;

/************************************************
 * Retrieve a single sport configuration by ID
 ************************************************/
CREATE OR REPLACE FUNCTION public.get_sport_configuration_by_id(p_id UUID)
RETURNS SETOF public.sportconfigurations AS $$
BEGIN
    RETURN QUERY
    SELECT * FROM public.sportconfigurations WHERE id = p_id;
END;$$ LANGUAGE plpgsql;

/*************************************************
 * Retrieves all available sports from the system.
 *************************************************/
CREATE OR REPLACE FUNCTION public.get_all_sports()
RETURNS SETOF public.sports AS $$
BEGIN
    RETURN QUERY
    SELECT * FROM public.sports
    ORDER BY name ASC;
END;$$ LANGUAGE plpgsql;

/******************************************************************
 * Retrieves all configurations associated with a specific sport.
 ******************************************************************/
CREATE OR REPLACE FUNCTION public.get_sport_configurations_by_sport_id(
    p_sport_id UUID
)
RETURNS SETOF public.sportconfigurations AS $$
BEGIN
    RETURN QUERY
    SELECT * FROM public.sportconfigurations
    WHERE sportid = p_sport_id;
END;$$ LANGUAGE plpgsql;

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

/************************************************************************************************
 * Function: public.create_quick_match
 * Description: Provisions JIT teams, tournament container, player rosters (capped by rosterlimit 
 *              for both Home and Guest teams), AND creates the match entity.
 *              Ensures base geography and default club exist JIT to guarantee idempotency.
 *              Requires an authenticated user identifier (p_user_id) to set as fallback owner.
 *              Assigns Global FullControl Admin as tournament owner if available.
 *              Falls back to sports.defaultconfigid if p_configuration_id is NULL.
 *              Returns full match entity record matching public.matches structure.
 ************************************************************************************************/
CREATE OR REPLACE FUNCTION public.create_quick_match(
    p_sport_id UUID,
    p_user_id VARCHAR(64),
    p_configuration_id UUID DEFAULT NULL
)
RETURNS SETOF public.matches
LANGUAGE plpgsql
AS $$
#variable_conflict use_column
DECLARE
    v_club_id UUID := '11111111-1111-1111-1111-000000000001';
    v_city_id UUID := '11111111-1111-1111-1111-111111111111';
    v_owner_id VARCHAR(64);
    v_country_id INT;
    v_region_id INT;
    v_effective_config_id UUID;
    v_home_team_id UUID;
    v_guest_team_id UUID;
    v_tournament_id UUID;
    v_match_id UUID := gen_random_uuid();
    v_now TIMESTAMP WITH TIME ZONE := CURRENT_TIMESTAMP;
    v_position_id UUID;
    v_roster_limit INT;
BEGIN
    -- Validation: Ensure user ID is provided
    IF p_user_id IS NULL OR trim(p_user_id) = '' THEN
        RAISE EXCEPTION 'User ID is required for quick match tournament creation.'
            USING ERRCODE = '22004'; -- Null Value Not Allowed
    END IF;

    -- 0. Resolve tournament owner: 
    -- Search for Global FullControl (Admin) policy (targettype = 0 AND role = 0)
    SELECT userid INTO v_owner_id
    FROM auth.accesspolicies
    WHERE targettype = 0 AND role = 0 AND (expiresat IS NULL OR expiresat > CURRENT_TIMESTAMP)
    ORDER BY createdat ASC
    LIMIT 1;

    -- Fallback to caller p_user_id if no Global Admin exists
    IF v_owner_id IS NULL THEN
        v_owner_id := p_user_id;
    END IF;

    -- 1. Ensure JIT base infrastructure (Geography, Base Club) exists if missing
    INSERT INTO public.countries (name, code)
    SELECT 'Ukraine', 'UA' WHERE NOT EXISTS (SELECT 1 FROM public.countries c WHERE c.name = 'Ukraine');
    
    SELECT c.id INTO v_country_id FROM public.countries c WHERE c.name = 'Ukraine' LIMIT 1;

    INSERT INTO public.regions (countryid, name)
    SELECT v_country_id, 'Dnipro Region' WHERE NOT EXISTS (SELECT 1 FROM public.regions r WHERE r.name = 'Dnipro Region' AND r.countryid = v_country_id);
    
    SELECT r.id INTO v_region_id FROM public.regions r WHERE r.name = 'Dnipro Region' AND r.countryid = v_country_id LIMIT 1;

    INSERT INTO public.cities (id, regionid, name)
    VALUES (v_city_id, v_region_id, 'Dnipro')
    ON CONFLICT DO NOTHING;

    INSERT INTO public.clubs (id, cityid, name, createdat)
    VALUES (v_club_id, v_city_id, 'TTA Training Club', v_now)
    ON CONFLICT DO NOTHING;

    -- 2. Determine effective configuration ID (Use provided or fallback to sports.defaultconfigid)
    IF p_configuration_id = '00000000-0000-0000-0000-000000000000'::uuid THEN
        p_configuration_id := NULL;
    END IF;

    v_effective_config_id := COALESCE(
        p_configuration_id, 
        (SELECT s.defaultconfigid FROM public.sports s WHERE s.id = p_sport_id)
    );

    IF v_effective_config_id IS NULL THEN
        RAISE EXCEPTION 'Configuration ID not provided and default configuration does not exist for sport %.', p_sport_id
            USING ERRCODE = 'P0005';
    END IF;

    -- Validate that effective configuration exists AND belongs to the specified sport, retrieve rosterlimit
    SELECT sc.rosterlimit INTO v_roster_limit
    FROM public.sportconfigurations sc 
    WHERE sc.id = v_effective_config_id AND sc.sportid = p_sport_id;

    IF v_roster_limit IS NULL THEN
        RAISE EXCEPTION 'Configuration % was not found or does not belong to sport %.', v_effective_config_id, p_sport_id
            USING ERRCODE = 'P0005';
    END IF;

    -- Concurrency Protection: Acquire transactional advisory lock scoped to sport and configuration
    PERFORM pg_advisory_xact_lock(hashtext(p_sport_id::text), hashtext(v_effective_config_id::text));

    -- 3. Ensure Home Squad team exists for the given sportId (with gender = 0)
    SELECT t.id INTO v_home_team_id
    FROM public.teams t
    WHERE t.clubid = v_club_id AND t.sportid = p_sport_id AND t.name = 'Home Squad'
    LIMIT 1;

    IF v_home_team_id IS NULL THEN
        v_home_team_id := gen_random_uuid();
        INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat)
        VALUES (v_home_team_id, v_club_id, p_sport_id, 'Home Squad', 0, v_now);
    END IF;

    -- 4. Ensure Opponent Squad team exists for the given sportId (with gender = 0)
    SELECT t.id INTO v_guest_team_id
    FROM public.teams t
    WHERE t.clubid = v_club_id AND t.sportid = p_sport_id AND t.name = 'Opponent Squad'
    LIMIT 1;

    IF v_guest_team_id IS NULL THEN
        v_guest_team_id := gen_random_uuid();
        INSERT INTO public.teams (id, clubid, sportid, name, gender, createdat)
        VALUES (v_guest_team_id, v_club_id, p_sport_id, 'Opponent Squad', 0, v_now);
    END IF;

    -- 5. Ensure Training Tournament exists for the effective configurationId
    SELECT t.id INTO v_tournament_id
    FROM public.tournaments t
    WHERE t.configurationid = v_effective_config_id AND t.name = 'Training & Friendly Matches'
    LIMIT 1;

    IF v_tournament_id IS NULL THEN
        v_tournament_id := gen_random_uuid();
        INSERT INTO public.tournaments (id, sportid, configurationid, cityid, ownerid, name, startdate, createdat)
        VALUES (v_tournament_id, p_sport_id, v_effective_config_id, v_city_id, v_owner_id, 'Training & Friendly Matches', CURRENT_DATE, v_now);
    END IF;

    -- 6. Get or create a default position definition for this sport
    SELECT ppd.id INTO v_position_id
    FROM public.playerpositiondefinitions ppd
    WHERE ppd.sportid = p_sport_id
    LIMIT 1;

    IF v_position_id IS NULL THEN
        v_position_id := gen_random_uuid();
        INSERT INTO public.playerpositiondefinitions (id, sportid, name, shortname)
        VALUES (v_position_id, p_sport_id, 'Universal', 'UNI');
    END IF;

    -- 7. Bulk-register up to rosterlimit players for HOME SQUAD
    WITH ranked_players AS (
        SELECT 
            p.id AS player_id,
            ROW_NUMBER() OVER (ORDER BY p.createdat, p.id) AS rn
        FROM public.players p
        WHERE p.homeclubid = v_club_id
    )
    INSERT INTO public.playerrosters (id, tournamentid, teamid, playerid, number, positionid, createdat)
    SELECT 
        gen_random_uuid(),
        v_tournament_id,
        v_home_team_id,
        rp.player_id,
        rp.rn::INT,
        v_position_id,
        v_now
    FROM ranked_players rp
    WHERE rp.rn <= v_roster_limit
    ON CONFLICT (tournamentid, playerid) DO NOTHING;

    -- 8. Bulk-register up to rosterlimit players for OPPONENT (GUEST) SQUAD
    WITH ranked_players AS (
        SELECT 
            p.id AS player_id,
            ROW_NUMBER() OVER (ORDER BY p.createdat, p.id) AS rn
        FROM public.players p
        WHERE p.homeclubid = v_club_id
    )
    INSERT INTO public.playerrosters (id, tournamentid, teamid, playerid, number, positionid, createdat)
    SELECT 
        gen_random_uuid(),
        v_tournament_id,
        v_guest_team_id,
        rp.player_id,
        (rp.rn - v_roster_limit)::INT,
        v_position_id,
        v_now
    FROM ranked_players rp
    WHERE rp.rn > v_roster_limit AND rp.rn <= (v_roster_limit * 2)
    ON CONFLICT (tournamentid, playerid) DO NOTHING;

    -- 9. Insert Match entity directly
    INSERT INTO public.matches (
        id,
        tournamentid,
        hometeamid,
        guestteamid,
        scheduledat,
        createdat
    )
    VALUES (
        v_match_id,
        v_tournament_id,
        v_home_team_id,
        v_guest_team_id,
        v_now,
        v_now
    );

    -- 10. Return full created match entity matching public.matches
    RETURN QUERY
    SELECT m.*
    FROM public.matches m
    WHERE m.id = v_match_id;
END;
$$;

/**********************************************************************************
 * Removes a match record from the database by its unique identifier.
 * Returns TRUE if the record was successfully deleted, FALSE otherwise.
 * Dynamic CASCADE constraints will clean up related lineups/events automatically.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.delete_match(
    p_id UUID
)
RETURNS BOOLEAN AS $$
BEGIN
    DELETE FROM public.matches
    WHERE id = p_id;

    RETURN FOUND;
END;
$$ LANGUAGE plpgsql;

-- =============================================================
-- MATCHLINEUP MANAGEMENT FUNCTIONS
-- =============================================================
/*****************************************************************************
 * Trigger function to automatically create team placeholders in matchlineups
 *****************************************************************************/
 -- We create two records so that team-specific events (like timeouts) can be attributed correctly
CREATE OR REPLACE FUNCTION public.fn_create_team_placeholders()
RETURNS TRIGGER AS $$
DECLARE
    -- Sentinel jersey numbers for team-level placeholder lineup rows.
    -- Kept negative so they can never collide with real jersey numbers
    -- and are easy to filter out via `number > 0`.
    c_home_placeholder CONSTANT INT := -1;
    c_guest_placeholder CONSTANT INT := -2;
BEGIN
    -- Placeholder for Home Team
    INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, positionid)
    VALUES (gen_random_uuid(), NEW.id, NULL, c_home_placeholder, NULL);

    -- Placeholder for Guest Team
    INSERT INTO public.matchlineups (id, matchid, playerrosterid, number, positionid)
    VALUES (gen_random_uuid(), NEW.id, NULL, c_guest_placeholder, NULL);

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_matches_after_insert
AFTER INSERT ON public.matches
FOR EACH ROW
EXECUTE FUNCTION public.fn_create_team_placeholders();

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
    p_positionid UUID
)
RETURNS SETOF public.matchlineups AS $$
DECLARE
    v_sport_id UUID;
    v_lineup_limit INT;
    v_current_count INT;
    v_team_id UUID;
    v_tournament_id UUID;
    v_roster_tournament_id UUID;
    v_final_id UUID := COALESCE(p_id, gen_random_uuid());
BEGIN
    -- 1. Resolve IDs and Tournament context
    SELECT teamid, tournamentid INTO v_team_id, v_roster_tournament_id 
    FROM public.playerrosters WHERE id = p_playerrosterid;
    
    SELECT tournamentid INTO v_tournament_id FROM public.matches WHERE id = p_matchid;

    -- 2. Validation: Tournament integrity
    IF v_tournament_id <> v_roster_tournament_id THEN
        RAISE EXCEPTION 'Player roster entry belongs to a different tournament.' USING ERRCODE = 'P0001';
    END IF;

    -- 3. Validation: Match participation
    IF NOT EXISTS (
        SELECT 1 FROM public.matches 
        WHERE id = p_matchid AND (hometeamid = v_team_id OR guestteamid = v_team_id)
    ) THEN
        RAISE EXCEPTION 'Player does not belong to any team participating in this match.' USING ERRCODE = 'P0001';
    END IF;

    -- 4. Concurrency Protection & Lineup Limit Check
    -- Lock is scoped to the specific Match + Team combination to prevent race conditions
    PERFORM pg_advisory_xact_lock(hashtext(p_matchid::text), hashtext(v_team_id::text));

    IF NOT EXISTS (SELECT 1 FROM public.matchlineups WHERE id = v_final_id) THEN
        SELECT sc.lineuplimit INTO v_lineup_limit
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

    -- 5. Atomic Upsert
    RETURN QUERY
    INSERT INTO public.matchlineups (
        id, matchid, playerrosterid, number, positionid
    )
    VALUES (
        v_final_id, p_matchid, p_playerrosterid, p_number, p_positionid
    )
    ON CONFLICT (id) DO UPDATE SET
        number = EXCLUDED.number,
        positionid = EXCLUDED.positionid
    RETURNING *;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Retrieves the match lineup for a specific team with player and position details.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.get_team_match_lineup(
    p_match_id UUID,
    p_team_id UUID
)
RETURNS TABLE (
    id UUID,
    matchid UUID,
    teamid UUID,
    playerrosterid UUID,
    firstname VARCHAR,
    lastname VARCHAR,
    number INT,
    positionid UUID,
    positionname VARCHAR
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        ml.id, ml.matchid, pr.teamid, ml.playerrosterid,
        p.firstname, p.lastname, ml.number,
        ml.positionid, ppd.name
    FROM public.matchlineups ml
    JOIN public.playerrosters pr ON ml.playerrosterid = pr.id
    JOIN public.players p ON pr.playerid = p.id
    JOIN public.playerpositiondefinitions ppd ON ml.positionid = ppd.id
    WHERE ml.matchid = p_match_id AND pr.teamid = p_team_id
    ORDER BY ml.number;
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

/***************************************************************************************
 * Copies a specific selection of players from the tournament roster to the match.
 * Initialized with roster defaults: jersey number, position, and starting flag = FALSE.
 * Uses ON CONFLICT to skip players already present in the match lineup.
 * p_player_roster_ids: Array of UUIDs from the playerrosters table.
 ***************************************************************************************/
CREATE OR REPLACE FUNCTION public.copy_team_roster_to_match_lineup(
    p_matchid UUID,
    p_teamid UUID,
    p_player_roster_ids UUID[]
)
RETURNS INT AS $$
DECLARE
    v_inserted_count INT;
    v_tournamentid UUID;
    v_lineup_limit INT;
    v_current_count INT;
    v_requested_count INT;
BEGIN
    -- 1. Resolve tournament ID and verify team participation
    SELECT tournamentid INTO v_tournamentid 
    FROM public.matches 
    WHERE id = p_matchid AND (hometeamid = p_teamid OR guestteamid = p_teamid);

    IF v_tournamentid IS NULL THEN
        RAISE EXCEPTION 'Team % does not belong to match % or match not found.', p_teamid, p_matchid 
        USING ERRCODE = 'P0001';
    END IF;

    -- 2. Concurrency Protection
    PERFORM pg_advisory_xact_lock(hashtext(p_matchid::text), hashtext(p_teamid::text));

    -- 3. Get lineup limit
    SELECT sc.lineuplimit INTO v_lineup_limit
    FROM public.matches m
    JOIN public.tournaments t ON m.tournamentid = t.id
    JOIN public.sportconfigurations sc ON t.configurationid = sc.id
    WHERE m.id = p_matchid;

    -- 4. Calculate total count after potential insertion
    v_requested_count := cardinality(p_player_roster_ids);
    
    SELECT COUNT(*) INTO v_current_count
    FROM public.matchlineups ml
    JOIN public.playerrosters pr ON ml.playerrosterid = pr.id
    WHERE ml.matchid = p_matchid 
      AND pr.teamid = p_teamid
      -- Do not count players that are already in lineup AND in the new selection
      AND ml.playerrosterid != ALL(p_player_roster_ids);

    IF (v_current_count + v_requested_count) > v_lineup_limit THEN
        RAISE EXCEPTION 'Total players (%) would exceed the lineup limit (%) for this team.', 
            (v_current_count + v_requested_count), v_lineup_limit
        USING ERRCODE = 'P0003';
    END IF;

    -- 5. Bulk insert from the provided array
    INSERT INTO public.matchlineups (
        id, matchid, playerrosterid, number, positionid
    )
    SELECT 
        gen_random_uuid(), 
        p_matchid, 
        pr.id, 
        pr.number, 
        pr.positionid 
    FROM public.playerrosters pr
    WHERE pr.id = ANY(p_player_roster_ids)
      AND pr.teamid = p_teamid 
      AND pr.tournamentid = v_tournamentid
    -- Updated to match the new unique constraint (matchid, playerrosterid, number)
    ON CONFLICT (matchid, playerrosterid, number) DO NOTHING;

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
    positionid UUID,
    positionname VARCHAR
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        ml.id, ml.matchid, pr.teamid, ml.playerrosterid,
        p.firstname, p.lastname, ml.number,
        ml.positionid, ppd.name
    FROM public.matchlineups ml
    JOIN public.playerrosters pr ON ml.playerrosterid = pr.id
    JOIN public.players p ON pr.playerid = p.id
    JOIN public.playerpositiondefinitions ppd ON ml.positionid = ppd.id
    WHERE ml.id = p_id;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Checks if a specific match lineup entry has any associated game events.
 * This is a safety check used to prevent data inconsistency before deletion.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.check_match_lineup_has_events(p_id UUID)
RETURNS BOOLEAN AS $$
BEGIN
    RETURN EXISTS (
        SELECT 1 
        FROM public.gameevents 
        WHERE matchlineupid = p_id
    );
END;$$ LANGUAGE plpgsql;

-- =============================================================
-- EVENT DEFINITION STORED FUNCTIONS
-- =============================================================

/**********************************************************************************
 * Retrieves all game event definitions for a specific match.
 * Resolves the sport context via matches -> tournaments -> eventdefinitions.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.get_match_event_definitions(
    p_match_id UUID
)
RETURNS TABLE (
    id UUID,
    sportid UUID,
    name VARCHAR,
    shortname VARCHAR,
    ispositive BOOLEAN,
    createdat TIMESTAMPTZ
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        ed.id,
        ed.sportid,
        ed.name,
        ed.shortname,
        ed.ispositive,
        ed.createdat
    FROM public.eventdefinitions ed
    INNER JOIN public.tournaments t ON ed.sportid = t.sportid
    INNER JOIN public.matches m ON t.id = m.tournamentid
    WHERE m.id = p_match_id
    ORDER BY ed.name ASC;
END;$$ LANGUAGE plpgsql;

-- =============================================================
-- GAME EVENTS STORED FUNCTIONS
-- =============================================================

/**********************************************************************************
 * Upserts a game event record and returns the resulting row.
 * Consistent with the project's repository pattern for entity mapping.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.upsert_game_event(
    p_id UUID,
    p_matchlineupid UUID,
    p_eventdefinitionid UUID,
    p_periodnumber INT,
    p_eventtimestamp TIMESTAMPTZ,
    p_normalizedmatchtime INTERVAL,
    p_isleadtogoal BOOLEAN,
    p_createdat TIMESTAMPTZ
)
RETURNS SETOF public.gameevents AS $$
BEGIN
    RETURN QUERY
    INSERT INTO public.gameevents (
        id, 
        matchlineupid, 
        eventdefinitionid, 
        periodnumber, 
        eventtimestamp, 
        normalizedmatchtime, 
        isleadtogoal, 
        createdat
    )
    VALUES (
        p_id, 
        p_matchlineupid, 
        p_eventdefinitionid, 
        p_periodnumber, 
        p_eventtimestamp, 
        p_normalizedmatchtime, 
        p_isleadtogoal, 
        p_createdat
    )
    ON CONFLICT (id) DO UPDATE SET
        matchlineupid = EXCLUDED.matchlineupid,
        eventdefinitionid = EXCLUDED.eventdefinitionid,
        periodnumber = EXCLUDED.periodnumber,
        eventtimestamp = EXCLUDED.eventtimestamp,
        normalizedmatchtime = EXCLUDED.normalizedmatchtime,
        isleadtogoal = EXCLUDED.isleadtogoal
    RETURNING *; -- Returns the full row including the correct 'id' column name
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Retrieves a single game event record by its primary key.
 * Strictly returns columns defined in the public.gameevents table.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.get_game_event_by_id(p_id UUID)
RETURNS TABLE (
    id UUID,
    matchlineupid UUID,
    eventdefinitionid UUID,
    periodnumber INT,
    eventtimestamp TIMESTAMPTZ,
    normalizedmatchtime INTERVAL,
    isleadtogoal BOOLEAN,
    createdat TIMESTAMPTZ
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        ge.id, 
        ge.matchlineupid, 
        ge.eventdefinitionid, 
        ge.periodnumber, 
        ge.eventtimestamp, 
        ge.normalizedmatchtime, 
        ge.isleadtogoal, 
        ge.createdat
    FROM public.gameevents ge
    WHERE ge.id = p_id;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Retrieves a single game event enriched with metadata.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.get_game_event_by_id_with_details(p_id UUID)
RETURNS TABLE (
    id UUID,
    matchlineupid UUID,
    eventdefinitionid UUID,
    eventname VARCHAR,
    ispositive BOOLEAN,
    periodnumber INT,
    eventtimestamp TIMESTAMPTZ,
    normalizedmatchtime INTERVAL,
    isleadtogoal BOOLEAN,
    playername TEXT,
    playernumber INT,
    teamid UUID,
    teamname VARCHAR
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        ge.id, ge.matchlineupid, ge.eventdefinitionid, ed.name AS eventname, ed.ispositive,
        ge.periodnumber, ge.eventtimestamp, ge.normalizedmatchtime, ge.isleadtogoal,
        (p.firstname || ' ' || p.lastname) AS playername, ml.number AS playernumber,
        pr.teamid, t.name AS teamname
    FROM public.gameevents ge
    JOIN public.eventdefinitions ed ON ge.eventdefinitionid = ed.id
    LEFT JOIN public.matchlineups ml ON ge.matchlineupid = ml.id
    LEFT JOIN public.playerrosters pr ON ml.playerrosterid = pr.id
    LEFT JOIN public.players p ON pr.playerid = p.id
    LEFT JOIN public.teams t ON pr.teamid = t.id
    WHERE ge.id = p_id;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Retrieves all events for a specific match.
 * Mapped to TTA.BusinessLogic.Features.GameEvents.DTOs.GameEventResponse.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.get_match_events(
    p_match_id UUID
)
RETURNS TABLE (
    id UUID,
    matchlineupid UUID,
    eventdefinitionid UUID,
    eventname VARCHAR,
    ispositive BOOLEAN,
    periodnumber INT,
    eventtimestamp TIMESTAMPTZ,
    normalizedmatchtime INTERVAL, -- Matches TimeSpan? in C#
    isleadtogoal BOOLEAN,
    playername TEXT,
    playernumber INT,
    teamid UUID,
    teamname VARCHAR
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        ge.id, 
        ge.matchlineupid, 
        ge.eventdefinitionid, 
        ed.name AS eventname, 
        ed.ispositive,
        ge.periodnumber, 
        ge.eventtimestamp, 
        ge.normalizedmatchtime, -- Now correctly maps to nullable TimeSpan?
        ge.isleadtogoal,
        -- Combined name from player record (remains nullable)
        (p.firstname || ' ' || p.lastname)::TEXT AS playername,
        ml.number AS playernumber,
        pr.teamid,
        t.name AS teamname
    FROM public.gameevents ge
    INNER JOIN public.eventdefinitions ed ON ge.eventdefinitionid = ed.id
    -- Mandatory join: ensures event is linked to a valid lineup entry
    INNER JOIN public.matchlineups ml ON ge.matchlineupid = ml.id
    LEFT JOIN public.playerrosters pr ON ml.playerrosterid = pr.id
    LEFT JOIN public.players p ON pr.playerid = p.id
    LEFT JOIN public.teams t ON pr.teamid = t.id
    WHERE ml.matchid = p_match_id
    ORDER BY ge.periodnumber ASC, ge.eventtimestamp ASC;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Removes a game event from the database.
 * Returns TRUE if the record was successfully deleted, FALSE otherwise.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.delete_game_event(
    p_id UUID
)
RETURNS BOOLEAN AS $$
BEGIN
    DELETE FROM public.gameevents
    WHERE id = p_id;

    -- Returns TRUE if a row was actually deleted, otherwise FALSE
    RETURN FOUND;
END;
$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Iterates through all match periods, computes the piecewise-linear time 
 * normalization coefficient (K), and batch updates the normalized match time 
 * for all game events belonging to a specific team.
 * Automatically processes team placeholders (jerseys -1 and -2) for team events.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.normalize_match_events_time(
    p_match_id UUID,
    p_team_id UUID
)
RETURNS VOID AS $$
DECLARE
    v_nominal_minutes INT;
    v_period RECORD;
    v_anchor RECORD;
    v_next_anchor RECORD;
    v_event RECORD;
    v_total_active_seconds DOUBLE PRECISION;
    v_k DOUBLE PRECISION;
    v_accumulated_seconds DOUBLE PRECISION;
    v_segment_end TIMESTAMPTZ;
    v_count INT;
    v_i INT;
BEGIN
    -- 1. Retrieve the nominal period duration from sport configuration
    SELECT sc.perioddurationminutes INTO v_nominal_minutes
    FROM public.matches m
    JOIN public.tournaments t ON m.tournamentid = t.id
    JOIN public.sportconfigurations sc ON t.configurationid = sc.id
    WHERE m.id = p_match_id;

    IF v_nominal_minutes IS NULL OR v_nominal_minutes <= 0 THEN
        RAISE EXCEPTION 'Invalid sport configuration or nominal period duration for match %', p_match_id;
    END IF;

    -- Check if time anchors are missing AND target team events actually exist
    IF NOT EXISTS (
        SELECT 1
        FROM public.timeanchors
        WHERE matchid = p_match_id
    ) THEN
        IF EXISTS (
            SELECT 1 
            FROM public.gameevents ge
            JOIN public.matchlineups ml ON ge.matchlineupid = ml.id
            LEFT JOIN public.playerrosters pr ON ml.playerrosterid = pr.id
            WHERE ml.matchid = p_match_id
              AND (
                  (ml.playerrosterid IS NOT NULL AND pr.teamid = p_team_id)
                  OR (ml.playerrosterid IS NULL AND ml.number = -1 AND (SELECT hometeamid FROM public.matches WHERE id = p_match_id) = p_team_id)
                  OR (ml.playerrosterid IS NULL AND ml.number = -2 AND (SELECT guestteamid FROM public.matches WHERE id = p_match_id) = p_team_id)
              )
        ) THEN
            RAISE EXCEPTION 'No time anchors found for match %, but target team events exist', p_match_id
                USING ERRCODE = 'P0001';
        END IF;
    END IF;

    -- 2. Loop through each period scope represented in the match anchors
    FOR v_period IN 
        SELECT DISTINCT periodnumber 
        FROM public.timeanchors 
        WHERE matchid = p_match_id
    LOOP
        v_total_active_seconds := 0;
        
        -- Create an isolated tracking session table for the current period anchors
        DROP TABLE IF EXISTS temp_period_anchors;
        CREATE TEMP TABLE temp_period_anchors AS
        SELECT type, timestamp, row_number() OVER (ORDER BY timestamp ASC) as row_num
        FROM public.timeanchors
        WHERE matchid = p_match_id AND periodnumber = v_period.periodnumber;
        
        SELECT COUNT(*) INTO v_count FROM temp_period_anchors;
        
        -- Calculate total active real-world play duration for the period coefficient
        IF v_count >= 2 THEN
            FOR v_i IN 1..(v_count - 1) LOOP
                SELECT type, timestamp INTO v_anchor FROM temp_period_anchors WHERE row_num = v_i;
                SELECT type, timestamp INTO v_next_anchor FROM temp_period_anchors WHERE row_num = v_i + 1;
                
                -- Segment is active if it opens with PeriodStart (0) or StoppageEnd (3)
                IF v_anchor.type = 0 OR v_anchor.type = 3 THEN
                    v_total_active_seconds := v_total_active_seconds + EXTRACT(EPOCH FROM (v_next_anchor.timestamp - v_anchor.timestamp));
                END IF;
            END LOOP;
        END IF;

        -- Ensure active play time exists if there are target team events to prevent data corruption
        IF v_total_active_seconds <= 0 THEN
            IF EXISTS (
                SELECT 1 
                FROM public.gameevents ge
                JOIN public.matchlineups ml ON ge.matchlineupid = ml.id
                LEFT JOIN public.playerrosters pr ON ml.playerrosterid = pr.id
                WHERE ml.matchid = p_match_id 
                  AND ge.periodnumber = v_period.periodnumber
                  AND (
                      (ml.playerrosterid IS NOT NULL AND pr.teamid = p_team_id)
                      OR (ml.playerrosterid IS NULL AND ml.number = -1 AND (SELECT hometeamid FROM public.matches WHERE id = p_match_id) = p_team_id)
                      OR (ml.playerrosterid IS NULL AND ml.number = -2 AND (SELECT guestteamid FROM public.matches WHERE id = p_match_id) = p_team_id)
                  )
            ) THEN
                RAISE EXCEPTION 'Cannot normalize match %, period % without active play time while target team events exist',
                    p_match_id, v_period.periodnumber
                    USING ERRCODE = 'P0001';
            END IF;
        END IF;

        -- Run calculations only if active play time is available and events exist
        IF v_total_active_seconds > 0 THEN
            v_k := (v_nominal_minutes * 60.0) / v_total_active_seconds;
            
            -- Iterate through all game events for the specified team and period scope
            FOR v_event IN
                SELECT ge.id, ge.eventtimestamp
                FROM public.gameevents ge
                JOIN public.matchlineups ml ON ge.matchlineupid = ml.id
                LEFT JOIN public.playerrosters pr ON ml.playerrosterid = pr.id
                WHERE ml.matchid = p_match_id 
                  AND ge.periodnumber = v_period.periodnumber
                  AND (
                      (ml.playerrosterid IS NOT NULL AND pr.teamid = p_team_id)
                      OR (ml.playerrosterid IS NULL AND ml.number = -1 AND (SELECT hometeamid FROM public.matches WHERE id = p_match_id) = p_team_id)
                      OR (ml.playerrosterid IS NULL AND ml.number = -2 AND (SELECT guestteamid FROM public.matches WHERE id = p_match_id) = p_team_id)
                  )
            LOOP
                v_accumulated_seconds := 0;
                
                -- Accumulate scaled clean time up to this specific event's timestamp
                FOR v_i IN 1..(v_count - 1) LOOP
                    SELECT type, timestamp INTO v_anchor FROM temp_period_anchors WHERE row_num = v_i;
                    SELECT type, timestamp INTO v_next_anchor FROM temp_period_anchors WHERE row_num = v_i + 1;
                    
                    -- Stop processing anchors if they start after the event took place
                    IF v_anchor.timestamp > v_event.eventtimestamp THEN
                        EXIT;
                    END IF;
                    
                    IF v_anchor.type = 0 OR v_anchor.type = 3 THEN
                        -- Cap the segment boundary at the event timestamp if it occurred mid-segment
                        IF v_next_anchor.timestamp < v_event.eventtimestamp THEN
                            v_segment_end := v_next_anchor.timestamp;
                        ELSE
                            v_segment_end := v_event.eventtimestamp;
                        END IF;
                        
                        IF v_segment_end > v_anchor.timestamp THEN
                            v_accumulated_seconds := v_accumulated_seconds + (EXTRACT(EPOCH FROM (v_segment_end - v_anchor.timestamp)) * v_k);
                        END IF;
                    END IF;
                END LOOP;
                
                -- Apply the final calculated interval to the target event row
                UPDATE public.gameevents
                SET normalizedmatchtime = v_accumulated_seconds * INTERVAL '1 second'
                WHERE id = v_event.id;
            END LOOP;
        END IF;
    END LOOP;
    
    DROP TABLE IF EXISTS temp_period_anchors;
END;$$ LANGUAGE plpgsql;

-- =============================================================
-- TIME ANCHORS STORED FUNCTIONS
-- =============================================================

/**********************************************************************************
 * Inserts or updates a time anchor record.
 * If the provided ID exists, updates the record. Otherwise, inserts a new one.
 * Returns the resulting row from the public.timeanchors table.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.upsert_time_anchor(
    p_id UUID,
    p_match_id UUID,
    p_period_number INT,
    p_type INT,
    p_timestamp TIMESTAMPTZ
)
RETURNS SETOF public.timeanchors AS $$
BEGIN
    RETURN QUERY
    INSERT INTO public.timeanchors (
        id,
        matchid,
        periodnumber,
        type,
        timestamp
    )
    VALUES (
        p_id,
        p_match_id,
        p_period_number,
        p_type,
        p_timestamp
    )
    ON CONFLICT (id) DO UPDATE SET
        matchid = EXCLUDED.matchid,
        periodnumber = EXCLUDED.periodnumber,
        type = EXCLUDED.type,
        timestamp = EXCLUDED.timestamp
    RETURNING *;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Retrieves a single time anchor record by its unique identifier.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.get_time_anchor_by_id(
    p_id UUID
)
RETURNS SETOF public.timeanchors AS $$
BEGIN
    RETURN QUERY
    SELECT * FROM public.timeanchors
    WHERE id = p_id;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Retrieves all time anchors associated with a specific match.
 * Results are ordered chronologically by period and timestamp.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.get_match_anchors(
    p_match_id UUID
)
RETURNS SETOF public.timeanchors AS $$
BEGIN
    RETURN QUERY
    SELECT * FROM public.timeanchors
    WHERE matchid = p_match_id
    ORDER BY periodnumber ASC, timestamp ASC;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Removes a specific time anchor record from the database.
 * Returns TRUE if the record was successfully deleted, FALSE otherwise.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.delete_time_anchor(
    p_id UUID
)
RETURNS BOOLEAN AS $$
BEGIN
    DELETE FROM public.timeanchors
    WHERE id = p_id;
    
    RETURN FOUND;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Retrieves the nominal period duration in minutes from the sport configuration
 * associated with a specific match identifier.
 * Traverses matches, tournaments, and sportconfigurations tables.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.get_match_period_duration_minutes(
    p_match_id UUID
)
RETURNS INT AS $$
DECLARE
    v_period_duration INT;
BEGIN
    SELECT sc.perioddurationminutes INTO v_period_duration
    FROM public.matches m
    JOIN public.tournaments t ON m.tournamentid = t.id
    JOIN public.sportconfigurations sc ON t.configurationid = sc.id
    WHERE m.id = p_match_id;

    RETURN COALESCE(v_period_duration, 0);
END;$$ LANGUAGE plpgsql;

-- =============================================================
-- PLAYER PRESENCE STORED FUNCTIONS & PROCEDURES
-- =============================================================

/**********************************************************************************
 * Inserts a new player presence record or updates an existing one (e.g., setting timeout).
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.record_player_presence(
    p_id UUID,
    p_match_lineup_id UUID,
    p_period_number INT,
    p_time_in TIMESTAMPTZ,
    p_time_out TIMESTAMPTZ DEFAULT NULL
)
RETURNS SETOF public.playerpresences AS $$
BEGIN
    RETURN QUERY
    INSERT INTO public.playerpresences (id, matchlineupid, periodnumber, timein, timeout)
    VALUES (p_id, p_match_lineup_id, p_period_number, p_time_in, p_time_out)
    ON CONFLICT (id) DO UPDATE 
    SET timeout = EXCLUDED.timeout
    RETURNING *;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Retrieves all player presence records for a specific match.
 * Joins with matchlineups to filter by matchid. Ordered chronologically.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.get_match_presence(
    p_match_id UUID
)
RETURNS SETOF public.playerpresences AS $$
BEGIN
    RETURN QUERY
    SELECT pp.* FROM public.playerpresences pp
    JOIN public.matchlineups ml ON pp.matchlineupid = ml.id
    WHERE ml.matchid = p_match_id
    ORDER BY pp.periodnumber ASC, pp.timein ASC;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Bulk inserts presence records with client-generated IDs and TimeIn timestamp.
 * Deduplicates the input array by lineup ID to prevent duplicate active records.
 * Handles idempotency via ON CONFLICT (id) DO NOTHING.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.init_period_presence(
    p_period_number INT,
    p_time_in TIMESTAMPTZ,
    p_ids UUID[],
    p_lineup_ids UUID[]
)
RETURNS VOID AS $$
BEGIN
    INSERT INTO public.playerpresences (id, matchlineupid, periodnumber, timein)
    SELECT item.id, item.lineup_id, p_period_number, p_time_in
    FROM (
        SELECT DISTINCT ON (lineup_id) id, lineup_id
        FROM unnest(p_ids, p_lineup_ids) AS t(id, lineup_id)
    ) AS item
    ON CONFLICT (id) DO NOTHING;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Automatically updates the timeout for all currently active players in a match period
 * when that specific period ends.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.close_active_presences(
    p_match_id UUID,
    p_period_number INT,
    p_time_out TIMESTAMPTZ
)
RETURNS VOID AS $$
BEGIN
    UPDATE public.playerpresences pp
    SET timeout = p_time_out
    FROM public.matchlineups ml
    WHERE pp.matchlineupid = ml.id
      AND ml.matchid = p_match_id
      AND pp.periodnumber = p_period_number
      AND pp.timeout IS NULL;
END;$$ LANGUAGE plpgsql;

/**********************************************************************************
 * Calculates the total elapsed linear seconds ("dirty" time) spent by each player 
 * of a specific team in a selected match, grouped by period.
 * Returns the exact seconds as DOUBLE PRECISION for clean .NET TimeSpan mapping.
 **********************************************************************************/
CREATE OR REPLACE FUNCTION public.calculate_players_dirty_time_by_period(
    p_match_id UUID,
    p_team_id UUID
)
RETURNS TABLE (
    matchlineupid UUID,
    periodnumber INT,
    dirtyseconds DOUBLE PRECISION
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        ml.id AS matchlineupid,
        pp.periodnumber,
        COALESCE(SUM(EXTRACT(EPOCH FROM (pp.timeout - pp.timein))), 0)::DOUBLE PRECISION AS dirtyseconds
    FROM public.playerpresences pp
    JOIN public.matchlineups ml ON pp.matchlineupid = ml.id
    LEFT JOIN public.playerrosters pr ON ml.playerrosterid = pr.id
    WHERE ml.matchid = p_match_id
      AND (
          pr.teamid = p_team_id
          OR (ml.playerrosterid IS NULL AND ml.number = -1 AND (SELECT hometeamid FROM public.matches WHERE id = p_match_id) = p_team_id)
          OR (ml.playerrosterid IS NULL AND ml.number = -2 AND (SELECT guestteamid FROM public.matches WHERE id = p_match_id) = p_team_id)
      )
    GROUP BY ml.id, pp.periodnumber;
END;$$ LANGUAGE plpgsql;