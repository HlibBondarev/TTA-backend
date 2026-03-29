-- =============================================================
-- AUTHENTICATION & AUTHORIZATION STORED FUNCTIONS & PROCEDURES
-- =============================================================

-- Retrieves the effective user role for a specific target or global scope.
-- Optimized to handle role precedence and explicit schema qualification.
CREATE OR REPLACE FUNCTION auth.get_user_permission(
    p_user_id VARCHAR(64),
    p_target_type VARCHAR(20),
    p_target_id UUID DEFAULT NULL
)
RETURNS VARCHAR(20) AS $$DECLARE
    v_role VARCHAR(20);
BEGIN
    -- Switched to lowercase column names to match the new table definitions
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

    RETURN v_role;
END;$$ LANGUAGE plpgsql;

-- ====================================================
-- ORGANIZATIONS & TEAMS STORED FUNCTIONS & PROCEDURES
-- ====================================================

-- Creates a club and its associated FullControl policy in a single transaction.
CREATE OR REPLACE FUNCTION auth.create_club_with_ownership(
    p_id UUID,
    p_city_id UUID,
    p_name TEXT,
    p_owner_id TEXT,
    p_created_at TIMESTAMPTZ -- Handles UTC from C# correctly
)
RETURNS UUID AS $$BEGIN
    -- Insert into public.clubs (using lowercase columns)
    INSERT INTO public.clubs (id, cityid, name, createdat)
    VALUES (p_id, p_city_id, p_name, p_created_at);

    -- Insert into auth.accesspolicies (using lowercase columns)
    INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat)
    VALUES (gen_random_uuid(), p_owner_id, 'FullControl', 'Club', p_id, p_created_at);

    RETURN p_id;
END;$$ LANGUAGE plpgsql;

-- Checks if a user already owns any club to enforce "one club per user" rule.
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
    );
END;$$ LANGUAGE plpgsql;