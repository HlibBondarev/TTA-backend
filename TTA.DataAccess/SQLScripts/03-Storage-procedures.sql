-- =============================================================
-- AUTHENTICATION & AUTHORIZATION STORED FUNCTIONS & PROCEDURES
-- =============================================================

-- 1) Retrieves the effective user role for a specific target or global scope.
-- Returns an INTEGER mapping to C# AppRole (0: FullControl, 1: Editor, 2: Viewer).
-- Logic: Checks direct policies first, then derives team-level access from active memberships.

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
    -- 1. Check direct policies (Global or Direct Club level)
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

    -- 2. If no direct policy found and target is a Team, derive from membership or inherited Club policy
    IF v_role IS NULL AND p_target_type = 2 THEN
        -- Resolve parent club for inheritance check
        SELECT clubid INTO v_club_id FROM public.teams WHERE id = p_target_id;

        SELECT role INTO v_role
        FROM (
            -- Inherited from parent Club policy
            SELECT role FROM auth.accesspolicies 
            WHERE userid = p_user_id AND targettype = 1 AND targetid = v_club_id
              AND (expiresat IS NULL OR expiresat > CURRENT_TIMESTAMP)
            UNION ALL
            -- Derived from active Team Membership
            -- Note: We check 'leftat' to ensure the membership is still active.
            -- Using 0 (FullControl) ensures the member has administrative rights for their team.
            SELECT 0 
            FROM public.teammemberships 
            WHERE userid = p_user_id AND teamid = p_target_id AND leftat IS NULL
        ) AS combined_roles
        ORDER BY role ASC LIMIT 1;
    END IF;

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

-- Enforce Business Rule 1 at the DB level to prevent concurrent-insert races.
-- This ensures only one active role of a specific type exists per user in a team.
CREATE UNIQUE INDEX IF NOT EXISTS uix_teammemberships_active_role_per_team 
ON public.teammemberships (teamid, userid, roleinteam) 
WHERE (leftat IS NULL);

-- 1) Upserts a team membership with strict integrity checks:
-- a. Prevents duplicate active roles for the same user in a team.
-- b. Ensures only one membership is marked as 'isprimary' for the user across all teams.
-- c. Uses p_approle to maintain a matching record in auth.accesspolicies for standard lookups.

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
-- for a specific membership. Access is revoked automatically via get_user_permission.
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