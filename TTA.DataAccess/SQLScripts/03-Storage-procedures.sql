-- ======================================================
-- AUTHENTICATION & AUTHORIZATION STORED FUNCTIONS
-- ======================================================

-- Retrieves the effective user role for a specific target or global scope.
-- Used by AccessRepository.GetUserRoleForScope via StoredProcedure command type.
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
    FROM AccessPolicies
    WHERE UserId = p_user_id
      AND (
          -- Check for global administrator privileges first
          (TargetType = 'Global') 
          OR 
          -- Check for specific resource access (Club or Team)
          (TargetType = p_target_type AND TargetId = p_target_id)
      )
      AND (ExpiresAt IS NULL OR ExpiresAt > CURRENT_TIMESTAMP)
    ORDER BY (CASE WHEN TargetType = 'Global' THEN 0 ELSE 1 END) -- Global role takes precedence
    LIMIT 1;

    RETURN v_role;
END;
$$ LANGUAGE plpgsql;