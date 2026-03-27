-- ======================================================
-- AUTHENTICATION & AUTHORIZATION STORED FUNCTIONS
-- ======================================================

-- Retrieves the effective user role for a specific target or global scope.
-- Optimized to handle role precedence and explicit schema qualification.
CREATE OR REPLACE FUNCTION auth.get_user_permission(
    p_user_id VARCHAR(64),
    p_target_type VARCHAR(20),
    p_target_id UUID DEFAULT NULL
)
RETURNS VARCHAR(20) AS $$
DECLARE
    v_role VARCHAR(20);
BEGIN
    SELECT "Role" INTO v_role
    -- Explicitly using auth schema to remove search_path dependency
    FROM auth.AccessPolicies 
    WHERE "UserId" = p_user_id
      AND (
          -- Check for global administrator privileges
          ("TargetType" = 'Global') 
          OR 
          -- Check for specific resource access (Club or Team)
          -- TargetId matches the naming convention in 01-Tables.sql
          ("TargetType" = p_target_type AND "TargetId" = p_target_id)
      )
      AND ("ExpiresAt" IS NULL OR "ExpiresAt" > CURRENT_TIMESTAMP)
    ORDER BY 
        -- 1. Role strength precedence (FullControl > Editor > Viewer)
        (CASE 
            WHEN "Role" = 'FullControl' THEN 0 
            WHEN "Role" = 'Editor' THEN 1 
            WHEN "Role" = 'Viewer' THEN 2 
            ELSE 3 
         END) ASC,
        -- 2. Tie-break: prefer scoped access over Global if roles are identical
        (CASE 
            WHEN "TargetType" = 'Global' THEN 1 
            ELSE 0 
         END) ASC
    LIMIT 1;

    RETURN v_role;
END;
$$ LANGUAGE plpgsql;