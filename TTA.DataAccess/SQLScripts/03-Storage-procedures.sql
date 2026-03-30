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
-- ORGANIZATIONS & TEAMS STORED FUNCTIONS & PROCEDURES
-- ====================================================

-- 1) Creates a club and its associated FullControl policy in a single transaction.
-- The ID for the access policy is generated automatically by the table default.
CREATE OR REPLACE FUNCTION auth.create_club_with_ownership(
    p_id UUID,
    p_cityid UUID,
    p_name TEXT,
    p_ownerid TEXT,
    p_createdat TIMESTAMPTZ
) RETURNS UUID AS $$
DECLARE
    v_constraint_name TEXT;
BEGIN
    -- 1. Insert the club record
    INSERT INTO public.clubs (id, cityid, name, createdat)
    VALUES (p_id, p_cityid, p_name, p_createdat);

    -- 2. Insert the ownership policy
    INSERT INTO auth.accesspolicies (userid, targettype, targetid, role, createdat)
    VALUES (p_ownerid, 'Club', p_id, 'FullControl', p_createdat);

    RETURN p_id;

EXCEPTION 
    WHEN unique_violation THEN
        GET STACKED DIAGNOSTICS v_constraint_name = CONSTRAINT_NAME;
        IF v_constraint_name = 'uix_accesspolicies_club_owner' THEN
            RAISE EXCEPTION 'User already owns a club.' USING ERRCODE = '23505';
        ELSE
            RAISE;
        END IF;
END;$$ LANGUAGE plpgsql;

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