namespace TTA.DataAccess.Repository;

public static class SqlStatements
{
    /// <summary>
    /// SQL constants for User-related database operations.
    /// </summary>
    public static class ForUsers
    {
        /// <summary>
        /// SQL to retrieve a single user by ID.
        /// </summary>
        public const string GetUserById =
            "SELECT * FROM public.get_user_by_id(@p_id)";

        /// <summary>
        /// SQL to retrieve a User by email.
        /// </summary>
        public const string GetUsersByEmail =
            "SELECT * FROM public.get_users_by_email(@p_email)";
    }

    /// <summary>
    /// SQL constants for AccessPolicies-related database operations.
    /// </summary>
    public static class ForAccessPolicies
    {
        /// <summary>
        /// Name of the PostgreSQL function to get a user permission.
        /// </summary>
        public const string GetUserPermission =
            "SELECT auth.get_user_permission(@UserId, @TargetType, @TargetId)";
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
           "SELECT auth.create_club_with_ownership(@Id, @CityId, @Name, @OwnerId, @OwnerEmail, @OwnerName, @CreatedAt)";

        /// <summary>
        /// Name of the PostgreSQL function to check if a specific user owns any club.
        /// </summary>
        public const string CheckUserOwnsAnyClub =
            "SELECT auth.check_user_owns_any_club(@UserId)";
    }

    /// <summary>
    /// SQL constants for Team-related database operations.
    /// </summary>
    public static class ForTeams
    {
        /// <summary>
        /// SQL to call the upsert function and return the resulting team record.
        /// </summary>
        public const string UpsertTeam =
            "SELECT * FROM public.upsert_team(@Id, @ClubId, @SportId, @Name, @MinBirthYear, @Gender, @CreatedAt)";

        /// <summary>
        /// SQL to retrieve all teams for a specific club.
        /// </summary>
        public const string GetTeamsByClub =
            "SELECT * FROM public.get_teams_by_club(@p_club_id)";

        /// <summary>
        /// SQL to retrieve a single team by ID.
        /// </summary>
        public const string GetTeamById =
            "SELECT * FROM public.get_team_by_id(@p_id)";
    }

    /// <summary>
    /// SQL constants for TeamMembership-related database operations.
    /// </summary>
    public static class ForTeamMemberships
    {
        /// <summary>
        /// SQL to call the upsert function for team membership.
        /// </summary>
        public const string UpsertMembership =
        "SELECT * FROM public.upsert_team_membership(@Id, @UserId, @TeamId, @RoleInTeam, @IsPrimary, @AppRole, @JoinedAt)";

        /// <summary>
        /// SQL to call the termination function with team-scoped validation.
        /// </summary>
        public const string TerminateMembership =
            "SELECT public.terminate_team_membership(@p_team_id, @p_membership_id)";

        /// <summary>
        /// SQL to retrieve active team members with user details in JSON format.
        /// </summary>
        public const string GetMembersJson =
            "SELECT public.get_team_members_json(@p_team_id)";

        /// <summary>
        /// SQL to retrieve a single membership by ID.
        /// </summary>
        public const string GetMembershipById =
            "SELECT * FROM public.teammemberships WHERE id = @p_id";
    }

    /// <summary>
    /// SQL constants for Player-related database operations.
    /// </summary>
    public static class ForPlayers
    {
        /// <summary>
        /// SQL to call the upsert function and return the resulting player record.
        /// </summary>
        public const string UpsertPlayer =
            "SELECT * FROM public.upsert_player(@Id, @HomeClubId, @FirstName, @LastName, @BirthDate, @Gender, @CreatedAt)";

        /// <summary>
        /// SQL to retrieve all players for a specific club.
        /// </summary>
        public const string GetPlayersByClub =
            "SELECT * FROM public.get_players_by_club(@p_club_id)";

        /// <summary>
        /// SQL to retrieve a single player by ID.
        /// </summary>
        public const string GetPlayerById =
            "SELECT * FROM public.get_player_by_id(@p_id)";

        /// <summary>
        /// SQL to delete a player.
        /// </summary>
        public const string DeletePlayer =
            "DELETE FROM public.players WHERE id = @p_id";
    }
}
