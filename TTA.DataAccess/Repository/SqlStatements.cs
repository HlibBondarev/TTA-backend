namespace TTA.DataAccess.Repository;

public static class SqlStatements
{
    /// <summary>
    /// SQL constants for AccessPolicies-related database operations.
    /// </summary>
    public static class ForAccessPolicies
    {
        /// <summary>
        /// Name of the PostgreSQL function to get a user permission.
        /// </summary>
        public const string GetUserPermission =
            "SELECT auth.get_user_permission(@userid, @targettype, @targetid)";
    }

    /// <summary>
    /// SQL constants for Club-related database operations.
    /// </summary>
    public static class ForClubs
    {
        /// <summary>
        /// Name of the PostgreSQL function to create a club with ownership.
        /// </summary>
        public const string CreateClubWithOwnership =
            "SELECT auth.create_club_with_ownership(@id, @cityid, @name, @ownerid, @createdat)";

        /// <summary>
        /// Name of the PostgreSQL function to check if a specific user owns any club.
        /// </summary>
        public const string CheckUserOwnsAnyClub =
            "SELECT auth.check_user_owns_any_club(@userId)";
    }

    public static class ForPlayers
    {
        /// <summary>
        /// SQL to call the upsert function and return the resulting player record.
        /// </summary>
        public const string UpsertPlayer =
            "SELECT * FROM upsert_player(@Id, @HomeClubId, @FirstName, @LastName, @BirthDate, @Gender, @CreatedAt)";

        /// <summary>
        /// SQL to retrieve all players for a specific club.
        /// </summary>
        public const string GetPlayersByClub =
            "SELECT * FROM get_players_by_club(@p_club_id)";

        /// <summary>
        /// SQL to retrieve a single player by ID.
        /// </summary>
        public const string GetPlayerById =
            "SELECT * FROM get_player_by_id(@p_id)";

        /// <summary>
        /// SQL to delete a player.
        /// </summary>
        public const string DeletePlayer =
            "SELECT delete_player(@p_id)";
    }
}
