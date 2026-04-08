-- =============================================================
-- AUTHENTICATION & AUTHORIZATION STORED FUNCTIONS & PROCEDURES
-- =============================================================

-- 1) Retrieves the effective user role for a specific target or global scope.
-- Returns an INTEGER that maps directly to the C# AppRole enum (0: FullControl, 1: Editor, 2: Viewer).

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
    -- If target is a Team, find its parent Club to check for inherited permissions
    IF p_target_type = 2 THEN
        SELECT clubid INTO v_club_id FROM public.teams WHERE id = p_target_id;
    END IF;

    SELECT role INTO v_role
    FROM auth.accesspolicies 
    WHERE userid = p_user_id 
      AND (
          (targettype = 0) -- Global level
          OR 
          (targettype = p_target_type AND targetid = p_target_id) -- Direct target level
          OR
          (p_target_type = 2 AND targettype = 1 AND targetid = v_club_id) -- Inherited from Club to Team
      )
      AND (expiresat IS NULL OR expiresat > CURRENT_TIMESTAMP)
    ORDER BY 
        role ASC, -- Priority to highest privilege (0: FullControl)
        targettype ASC -- Priority: Global(0) > Club(1) > Team(2)
    LIMIT 1;

    RETURN v_role;
END;$$ LANGUAGE plpgsql;

-- ==========================================
-- GEOGRAPHY & USERS
-- ==========================================

-- 1) Retrieve a single user by ID

CREATE OR REPLACE FUNCTION public.get_user_by_id(p_id TEXT)
RETURNS SETOF public.users AS $$BEGIN
    RETURN QUERY
    SELECT * FROM public.users 
    WHERE id = p_id;
END;$$ LANGUAGE plpgsql;

-- 2) Retrieves all users with a specific email.

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

-- 1) Atomic function to ensure user existence and create a club with ownership.
-- Maintains strict 'one club per owner' rule via unique constraint validation.

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

-- 2) Checks if a user already owns any club to enforce "one club per user" rule.

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

-- 1) Upserts a team record and returns the updated entity.

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

-- 2) Retrieves all teams associated with a specific club.

CREATE OR REPLACE FUNCTION public.get_teams_by_club(p_club_id UUID)
RETURNS SETOF public.teams AS $$
BEGIN
    RETURN QUERY
    SELECT * FROM public.teams 
    WHERE clubid = p_club_id
    ORDER BY name ASC;
END;
$$ LANGUAGE plpgsql;

-- 3) Retrieve a single team by ID

CREATE OR REPLACE FUNCTION public.get_team_by_id(p_id UUID)
RETURNS SETOF public.teams AS $$BEGIN
    RETURN QUERY
    SELECT * FROM public.teams WHERE id = p_id;
END;$$ LANGUAGE plpgsql;

-- ==========================================
-- TEAM MEMBERSHIP STORED FUNCTIONS
-- ==========================================

-- 1) Upserts a team membership with strict integrity checks:
-- a. Prevents duplicate active roles for the same user in a team.
-- b. Ensures only one membership is marked as 'isprimary' for the user across all teams.
-- c. Synchronizes access policies idempotently.

CREATE OR REPLACE FUNCTION public.upsert_team_membership_with_policy(
    p_id UUID,
    p_teamid UUID,
    p_userid VARCHAR(64),
    p_roleinteam INT,
    p_isprimary BOOLEAN,
    p_joinedat TIMESTAMPTZ,
    p_approle INT
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
    ) THEN
        RAISE EXCEPTION 'User already has an active membership with role % in this team.', p_roleinteam
        USING ERRCODE = '23505'; -- Unique violation
    END IF;

    -- Business Rule 2: Reset other primary flags if this new/updated membership is set to primary
    --                  Only affect active memberships (leftat IS NULL) to maintain historical data integrity.
    IF p_isprimary THEN
        UPDATE public.teammemberships
        SET isprimary = FALSE
        WHERE userid = p_userid 
            AND isprimary = TRUE 
            AND leftat IS NULL;
    END IF;

    -- 1. Insert the membership record
    INSERT INTO public.teammemberships (id, teamid, userid, roleinteam, isprimary, joinedat)
    VALUES (p_id, p_teamid, p_userid, p_roleinteam, p_isprimary, p_joinedat);

    -- 2. Synchronize access policies (Idempotent)
    -- uix_accesspolicies_user_target ensures we don't duplicate policies for the same team
    INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat)
    VALUES (gen_random_uuid(), p_userid, p_approle, 2, p_teamid, p_joinedat)
    ON CONFLICT ON CONSTRAINT uix_accesspolicies_user_target DO NOTHING;

    RETURN QUERY SELECT * FROM public.teammemberships WHERE id = p_id;
END;$$ LANGUAGE plpgsql;

-- 2) Retrieves all active members of a specific team in json-format.

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

-- 3) Updates the 'leftat' timestamp and resets 'isprimary' status 
-- for a specific membership to ensure data consistency.
CREATE OR REPLACE FUNCTION public.terminate_team_membership(
    p_team_id UUID,
    p_membership_id UUID
)
RETURNS BOOLEAN AS $$
DECLARE
    v_rows_affected INT;
BEGIN
    UPDATE public.teammemberships
    SET 
        -- Ensures leftat is at least equal to joinedat, even if clocks drift
        leftat = GREATEST(CURRENT_TIMESTAMP, joinedat),
        isprimary = FALSE
    WHERE id = p_membership_id 
      AND teamid = p_team_id 
      AND leftat IS NULL;

    GET DIAGNOSTICS v_rows_affected = ROW_COUNT;
    RETURN v_rows_affected > 0;
END;$$ LANGUAGE plpgsql;

-- ====================================================
-- PLAYERS STORED FUNCTIONS & PROCEDURES
-- ====================================================

-- 1) Retrieve a single player by ID

CREATE OR REPLACE FUNCTION public.get_player_by_id(p_id UUID)
RETURNS SETOF public.players AS $$BEGIN
    RETURN QUERY
    SELECT * FROM public.players WHERE id = p_id;
END;$$ LANGUAGE plpgsql;

-- 2) Upsert function for players: inserts a new player or updates existing one based on ID.

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
    -- 1. Validate gender input
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

-- 3) Retrieves all players associated with a specific club.

CREATE OR REPLACE FUNCTION public.get_players_by_club(p_club_id UUID)
RETURNS SETOF public.players AS $$BEGIN
    RETURN QUERY
    SELECT * FROM public.players 
    WHERE homeclubid = p_club_id;
END;$$ LANGUAGE plpgsql;