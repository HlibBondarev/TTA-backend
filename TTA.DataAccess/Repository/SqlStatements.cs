namespace TTA.DataAccess.Repository;

public static class SqlStatements
{
    public static class ForAccessPolicies
    {
        public const string GetUserPermission =
            "SELECT auth.get_user_permission(@userid, @targettype, @targetid)";
    }

    public static class ForClubs
    {
        public const string CreateClubWithOwnership =
            "SELECT auth.create_club_with_ownership(@id, @cityid, @name, @ownerid, @createdat)";
    }
}
