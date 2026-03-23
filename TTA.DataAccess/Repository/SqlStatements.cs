namespace TTA.DataAccess.Repository;

public static class SqlStatements
{
    public static class ForAccessPolicies
    {
        // Explicitly define the SELECT call for the PostgreSQL function
        public const string GetUserPermission = "SELECT auth.get_user_permission(@UserId, @Scope, @ResourceId)";
    }
}
