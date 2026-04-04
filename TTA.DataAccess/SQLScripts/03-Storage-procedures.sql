-- =============================================================
-- AUTHENTICATION & AUTHORIZATION STORED FUNCTIONS & PROCEDURES
-- =============================================================

-- 1) Retrieves the effective user role for a specific target or global scope.
-- Returns an INTEGER that maps directly to the C# AppRole enum (0, 1, 2).

CREATE OR REPLACE FUNCTION auth.get_user_permission(
    p_user_id VARCHAR(64),
    p_target_type VARCHAR(20),
    p_target_id UUID DEFAULT NULL
)
RETURNS INT AS $$
DECLARE
    v_role VARCHAR(20);
BEGIN
    -- Fetch the role name from access policies
    SELECT role INTO v_role
    FROM auth.accesspolicies 
    WHERE userid = p_user_id 
      AND (
          (targettype = 'Global') 
          OR 
          (targettype = p_target_type AND targetid = p_target_id)
      )
      AND (expiresat IS NULL OR expiresat > CURRENT_TIMESTAMP)
    ORDER BY 
        (CASE 
            WHEN role = 'FullControl' THEN 0 
            WHEN role = 'Editor' THEN 1 
            WHEN role = 'Viewer' THEN 2 
            ELSE 3 
         END) ASC,
        (CASE 
            WHEN targettype = 'Global' THEN 1 
            ELSE 0 
         END) ASC
    LIMIT 1;

    -- Map string role to integer for C# enum compatibility
    RETURN CASE 
        WHEN v_role = 'FullControl' THEN 0 
        WHEN v_role = 'Editor' THEN 1 
        WHEN v_role = 'Viewer' THEN 2 
        ELSE NULL 
    END;
END;$$ LANGUAGE plpgsql;

-- ====================================================
-- ORGANIZATIONS (CLUBS) STORED FUNCTIONS & PROCEDURES
-- ====================================================

-- 1) Atomic function to ensure user existence and create a club with ownership.
-- The ID for the access policy is generated automatically by the table default.
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
    -- Ensures the foreign key requirement is met if the user is new to the local DB.
    INSERT INTO public.users (id, email, displayname, createdat)
    VALUES (p_ownerid, p_owner_email, p_owner_name, p_createdat)
    ON CONFLICT (id) DO NOTHING;

    -- 2. Insert the club record
    INSERT INTO public.clubs (id, cityid, name, createdat)
    VALUES (p_id, p_cityid, p_name, p_createdat);

    -- 3. Insert the ownership policy
    -- This step triggers 'uix_accesspolicies_club_owner' if a policy already exists for this user.
    INSERT INTO auth.accesspolicies (userid, targettype, targetid, role, createdat)
    VALUES (p_ownerid, 'Club', p_id, 'FullControl', p_createdat);

    RETURN p_id;

EXCEPTION 
    WHEN unique_violation THEN
        GET STACKED DIAGNOSTICS v_constraint_name = CONSTRAINT_NAME;
        -- Explicit check for the unique ownership business rule constraint
        IF v_constraint_name = 'uix_accesspolicies_club_owner' THEN
            RAISE EXCEPTION 'User already owns a club.' USING ERRCODE = '23505';
        ELSE
            RAISE;
        END IF;
END;
$$ LANGUAGE plpgsql;

-- 2) Checks if a user already owns any club to enforce "one club per user" rule.
-- Updated to only consider active (non-expired) ownership policies.

CREATE OR REPLACE FUNCTION auth.check_user_owns_any_club(
    p_user_id TEXT
)
RETURNS BOOLEAN AS $$BEGIN
    RETURN EXISTS (
        SELECT 1 
        FROM auth.accesspolicies 
        WHERE userid = p_user_id 
          AND targettype = 'Club' 
          AND role = 'FullControl'
          -- FIX (Finding #10): Only count active ownerships matching the unique index logic
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
DECLARE
    v_gender_str VARCHAR(20);
BEGIN
    -- 1. Validate gender input
    IF p_gender NOT IN (0, 1) THEN
        RAISE EXCEPTION 'Invalid gender value: %. Expected 0 (Male) or 1 (Female).', p_gender 
        USING ERRCODE = '22023';
    END IF;

    -- 2. Map integer to string enum
    v_gender_str := CASE 
        WHEN p_gender = 0 THEN 'Male'
        WHEN p_gender = 1 THEN 'Female'
    END;

    RETURN QUERY
    INSERT INTO public.teams (id, clubid, sportid, name, minbirthyear, gender, createdat)
    VALUES (p_id, p_clubid, p_sportid, p_name, p_minbirthyear, v_gender_str, p_createdat)
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

-- ====================================================
-- PLAYERS STORED FUNCTIONS & PROCEDURES
-- ====================================================

-- 1) Retrieve a single player by ID (FIX: added public schema)

CREATE OR REPLACE FUNCTION get_player_by_id(p_id UUID)
RETURNS SETOF public.players AS $$BEGIN
    RETURN QUERY
    SELECT * FROM public.players WHERE id = p_id;
END;$$ LANGUAGE plpgsql;

-- 2) Upsert function for players: inserts a new player or updates existing one based on ID.

CREATE OR REPLACE FUNCTION upsert_player(
    p_id UUID,
    p_homeclubid UUID,
    p_firstname VARCHAR(100),
    p_lastname VARCHAR(100),
    p_birthdate DATE,
    p_gender INT,
    p_createdat TIMESTAMPTZ
)
RETURNS SETOF public.players AS $$DECLARE
    v_gender_str VARCHAR(20);
BEGIN
    -- 1. Validate gender input (Fail Fast as requested by Reviewer)
    IF p_gender NOT IN (0, 1) THEN
        RAISE EXCEPTION 'Invalid gender value: %. Expected 0 (Male) or 1 (Female).', p_gender 
        USING ERRCODE = '22023'; -- SQLSTATE for invalid_parameter_value
    END IF;

    -- 2. Map integer to string enum (no ELSE default here)
    v_gender_str := CASE 
        WHEN p_gender = 0 THEN 'Male'
        WHEN p_gender = 1 THEN 'Female'
    END;

    RETURN QUERY
    -- (FIX: added public schema)
    INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat)
    VALUES (p_id, p_homeclubid, p_firstname, p_lastname, p_birthdate, v_gender_str, p_createdat)
    ON CONFLICT (id) DO UPDATE SET
        homeclubid = EXCLUDED.homeclubid,
        firstname = EXCLUDED.firstname,
        lastname = EXCLUDED.lastname,
        birthdate = EXCLUDED.birthdate,
        gender = EXCLUDED.gender
    RETURNING *;
END;$$ LANGUAGE plpgsql;

-- 3) Retrieves all players associated with a specific club.

CREATE OR REPLACE FUNCTION get_players_by_club(p_club_id UUID)
RETURNS SETOF public.players AS $$BEGIN
    RETURN QUERY
    -- (FIX: added public schema)
    SELECT * FROM public.players 
    WHERE homeclubid = p_club_id;
END;$$ LANGUAGE plpgsql;