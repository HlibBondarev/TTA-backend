namespace TTA.DataAccess.Repository;

/// <summary>
/// Contains SQL command constants for invoking PostgreSQL storage functions.
/// </summary>
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
    /// Commands related to user access, roles, and authorization policies.
    /// </summary>
    public static class ForAccessPolicies
    {
        /// <summary>
        /// Invokes the universal upsert function for access policies.
        /// Used for both granting new permissions and revoking existing ones (by setting ExpiresAt).
        /// </summary>
        public const string UpsertAccessPolicy =
            "SELECT * FROM auth.upsert_access_policy(@Id, @UserId, @TargetType, @TargetId, @Role, @CreatedAt, @ExpiresAt)";

        /// <summary>
        /// Calls the authorization engine to retrieve the effective user role for a specific context.
        /// </summary>
        public const string GetUserPermission =
            "SELECT auth.get_user_permission(@UserId, @TargetType, @TargetId)";

        /// <summary>
        /// SQL to retrieve an active access policy for a user within a specific team context.
        /// </summary>
        public const string GetActiveTeamPolicy =
            "SELECT * FROM auth.get_active_team_policy(@UserId, @TeamId)";
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
    /// Commands for managing team memberships and player/staff assignments.
    /// </summary>
    public static class ForTeamMemberships
    {
        /// <summary>
        /// SQL to call the upsert function for team membership.
        /// </summary>
        public const string UpsertMembershipWithPolicy =
            "SELECT * FROM public.upsert_team_membership_with_policy(@Id, @TeamId, @UserId, @RoleInTeam, @IsPrimary, @JoinedAt, @AppRole)";

        /// <summary>
        /// SQL to retrieve active team members with user details in JSON format.
        /// </summary>
        public const string GetMembersJson =
            "SELECT public.get_team_members_json(@p_team_id)";

        /// <summary>
        /// Invokes the universal upsert function for team memberships.
        /// Handles role updates, primary flag changes, and termination via the LeftAt parameter.
        /// </summary>
        public const string UpsertMembership =
            "SELECT * FROM public.upsert_team_membership(@Id, @UserId, @TeamId, @RoleInTeam, @JoinedAt, @IsPrimary, @LeftAt)";

        /// <summary>
        /// Calls a specialized function to retrieve all active memberships (where LeftAt is NULL) 
        /// for a user identified by email within a specific team.
        /// </summary>
        public const string GetActiveByEmail =
            "SELECT * FROM public.get_active_memberships_by_email(@TeamId, @UserEmail)";

        /// <summary>
        /// Calls a specialized function to retrieve an active membership (where LeftAt is NULL) 
        /// for a user identified by email and role within a specific team.
        /// </summary>
        public const string GetActiveByEmailAndRole =
            "SELECT * FROM public.get_active_membership_by_email_and_role(@TeamId, @UserEmail, @RoleInTeam)";
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

    /// <summary>
    /// SQL constants for Tournament-related database operations.
    /// </summary>
    public static class ForTournaments
    {
        /// <summary>
        /// SQL to call the upsert function for tournaments and return the resulting record.
        /// Matches parameters of public.upsert_tournament.
        /// </summary>
        public const string UpsertTournament =
        "SELECT * FROM public.upsert_tournament(@Id, @SportId, @ConfigurationId, @CityId, @OwnerId, @Name, @StartDate, @EndDate, @CreatedAt)";

        /// <summary>
        /// SQL to retrieve a single tournament record by its ID using public.get_tournament_by_id.
        /// </summary>
        public const string GetTournamentById =
            "SELECT * FROM public.get_tournament_by_id(@p_id)";
    }

    /// <summary>
    /// Contains SQL command constants for invoking PostgreSQL storage functions related to Rosters.
    /// </summary>
    public static class ForRosters
    {
        /// <summary>
        /// SQL to call the upsert function for tournament rosters.
        /// Matches parameters of public.upsert_player_to_roster.
        /// </summary>
        public const string UpsertPlayerToRoster =
            "SELECT * FROM public.upsert_player_to_roster(@Id, @TournamentId, @TeamId, @PlayerId, @PositionId, @Number, @CreatedAt)";

        /// <summary>
        /// SQL to retrieve the team roster for a specific tournament.
        /// Calls public.get_tournament_team_roster.
        /// </summary>
        public const string GetTournamentTeamRoster =
            "SELECT * FROM public.get_tournament_team_roster(@TournamentId, @TeamId)";

        /// <summary>
        /// SQL to remove a player from a specific tournament roster by tournament and player IDs.
        /// </summary>
        public const string RemovePlayerFromRoster =
            "SELECT public.remove_player_from_roster(@TournamentId, @TeamId, @PlayerId)";
    }

    /// <summary>
    /// Contains SQL command constants for invoking PostgreSQL storage functions related to Matches.
    /// </summary>
    public static class ForMatches
    {
        /// <summary>
        /// SQL to call the upsert function for matches.
        /// </summary>
        public const string UpsertMatch = @"
            SELECT * FROM public.upsert_match(
                @Id, @TournamentId, @HomeTeamId, @GuestTeamId, @ScheduledAt, @MatchNumber, @Venue, @Temperature, @HomeScore, @GuestScore, @CreatedAt
            )";

        /// <summary>
        /// SQL to retrieve a match by its unique identifier.
        /// </summary>
        public const string GetMatchById =
            "SELECT * FROM public.get_match_by_id(@p_id)";

        /// <summary>
        /// SQL to retrieve all matches for a specific tournament.
        /// </summary>
        public const string GetTournamentMatches =
            "SELECT * FROM public.get_tournament_matches(@p_tournament_id)";

        /// <summary>
        /// SQL to call the storage function that returns match details by ID.
        /// </summary>
        public const string GetMatchWithDetailsById =
            "SELECT * FROM public.get_match_with_details_by_id(@p_id)";
    }

    /// <summary>
    /// Contains SQL command constants for Match Lineups operations.
    /// All commands execute stored procedures defined in the database schema.
    /// </summary>
    public static class ForMatchLineups
    {
        /// <summary>
        /// Executes upsert operation for a single match lineup entry.
        /// </summary>
        public const string UpsertLineupItem = @"
            SELECT * FROM public.upsert_match_lineup(
                @Id, @MatchId, @PlayerRosterId, @Number, @PositionId
            );";

        /// <summary>
        /// Retrieves the full protocol (lineup) for a specific match.
        /// </summary>
        public const string GetMatchLineup =
            "SELECT * FROM public.get_match_lineup(@p_id);";

        /// <summary>
        /// Removes a specific player from the match protocol using a storage function.
        /// </summary>
        public const string DeleteLineupItem =
            "SELECT public.delete_match_lineup_item(@p_id);";

        /// <summary>
        /// Executes the bulk copy of specific players from the tournament roster to a match protocol.
        /// Expected parameters: @p_matchid (UUID), @p_teamid (UUID), @p_player_roster_ids (UUID[]).
        /// </summary>
        public const string CopyRosterToLineup = @"
            SELECT public.copy_team_roster_to_match_lineup(
                @p_matchid, @p_teamid, @p_player_roster_ids
            );";

        /// <summary>
        /// Executes the function to retrieve a raw match lineup record by its ID.
        /// </summary>
        public const string GetById =
            "SELECT * FROM public.get_match_lineup_by_id(@p_id);";

        /// <summary>
        /// Executes the function to retrieve a match lineup entry with enriched player and position data.
        /// </summary>
        public const string GetByIdWithDetails =
            "SELECT * FROM public.get_match_lineup_details_by_id(@p_id);";

        /// <summary>
        /// Executes the function to check if a lineup entry is linked to any game events.
        /// </summary>
        public const string CheckHasEvents =
            "SELECT public.check_match_lineup_has_events(@p_id);";
    }

    /// <summary>
    /// SQL constants for Event Definition related database operations.
    /// </summary>
    public static class ForEventDefinitions
    {
        /// <summary>
        /// Invokes the storage function to retrieve all event definitions associated with the sport of a specific match.
        /// </summary>
        public const string GetMatchEventDefinitions =
            "SELECT * FROM public.get_match_event_definitions(@p_match_id);";
    }

    /// <summary>
    /// SQL command constants for Game Events related operations.
    /// These constants invoke storage functions defined in the public schema.
    /// </summary>
    public static class ForGameEvents
    {
        /// <summary>
        /// Invokes the storage function to create or update a game event.
        /// Uses 'SELECT * FROM' to ensure PostgreSQL returns columns that Dapper can map to the entity.
        /// Parameters match the property names of the GameEvent class for automatic mapping.
        /// </summary>
        public const string UpsertEvent = @"
            SELECT * FROM public.upsert_game_event(
                @Id, 
                @MatchLineupId, 
                @EventDefinitionId, 
                @PeriodNumber, 
                @EventTimestamp, 
                @NormalizedMatchTime, 
                @IsLeadToGoal, 
                @CreatedAt
            );";

        /// <summary>
        /// Invokes the storage function to retrieve a raw game event record by its unique identifier.
        /// Returns columns matching the public.gameevents table.
        /// </summary>
        public const string GetById =
            "SELECT * FROM public.get_game_event_by_id(@p_id);";

        /// <summary>
        /// SQL statement to invoke the detailed game event retrieval function.
        /// </summary>
        public const string GetByIdWithDetails =
            "SELECT * FROM public.get_game_event_by_id_with_details(@p_id);";

        /// <summary>
        /// Invokes the storage function to retrieve the full chronological timeline of events for a specific match.
        /// Returns events enriched with player names, team names, and event type metadata.
        /// </summary>
        public const string GetMatchEvents =
            "SELECT * FROM public.get_match_events(@p_match_id);";

        /// <summary>
        /// Invokes the storage function to permanently remove a game event record.
        /// </summary>
        public const string DeleteEvent =
            "SELECT public.delete_game_event(@p_id);";

        /// <summary>
        /// Invokes the batch storage function to calculate and update normalized match time for a team's events.
        /// </summary>
        public const string NormalizeMatchEventsTime =
            "SELECT public.normalize_match_events_time(@p_match_id, @p_team_id);";
    }

    /// <summary>
    /// SQL constants for Time Anchor related database operations.
    /// Used for piecewise-linear time normalization in match timelines.
    /// </summary>
    public static class ForTimeAnchors
    {
        /// <summary>
        /// Invokes the storage function to insert or update a time anchor.
        /// Returns the full record from the public.timeanchors table.
        /// </summary>
        public const string UpsertTimeAnchor =
            @"SELECT * FROM public.upsert_time_anchor(
                @Id, 
                @MatchId, 
                @PeriodNumber, 
                @Type, 
                @Timestamp
            );";

        /// <summary>
        /// Invokes the storage function to retrieve a specific time anchor by its unique identifier.
        /// </summary>
        public const string GetById =
            "SELECT * FROM public.get_time_anchor_by_id(@p_id);";

        /// <summary>
        /// Invokes the storage function to retrieve all time anchors for a specific match.
        /// Results are ordered chronologically by the database function.
        /// </summary>
        public const string GetMatchAnchors =
            "SELECT * FROM public.get_match_anchors(@p_match_id);";

        /// <summary>
        /// Invokes the storage function to permanently remove a time anchor record.
        /// </summary>
        public const string DeleteAnchor =
            "SELECT public.delete_time_anchor(@p_id);";

        /// <summary>
        /// Invokes the storage function to retrieve the nominal period duration in minutes for a specific match.
        /// </summary>
        public const string GetMatchPeriodDuration =
            "SELECT public.get_match_period_duration_minutes(@p_match_id);";
    }

    /// <summary>
    /// SQL constants for Player Presence related database operations.
    /// Used for tracking active playing time and substitutions.
    /// </summary>
    public static class ForPlayerPresence
    {
        /// <summary>
        /// Invokes the storage function to insert or update a player presence record.
        /// </summary>
        public const string RecordPresence =
            @"SELECT * FROM public.record_player_presence(
                @Id, 
                @MatchLineupId, 
                @PeriodNumber, 
                @TimeIn, 
                @TimeOut
            );";

        /// <summary>
        /// Invokes the storage function to retrieve all presence records for a specific match.
        /// </summary>
        public const string GetMatchPresence =
            "SELECT * FROM public.get_match_presence(@p_match_id);";

        /// <summary>
        /// Invokes the storage function to bulk insert explicit client presence IDs and lineup IDs for period start.
        /// </summary>
        public const string InitializePeriodPresence =
            "SELECT public.init_period_presence(@p_period_number, @p_time_in, @p_ids, @p_lineup_ids);";

        /// <summary>
        /// Invokes the storage function to automatically set the timeout for all active players when a period finishes.
        /// </summary>
        public const string CloseActivePresences =
            "SELECT public.close_active_presences(@p_match_id, @p_period_number, @p_time_out);";

        /// <summary>
        /// Invokes the storage function to calculate raw dirty play time by period for a specific team in a match.
        /// </summary>
        public const string CalculatePlayersDirtyTimeByPeriod =
            "SELECT * FROM public.calculate_players_dirty_time_by_period(@p_match_id, @p_team_id);";
    }
}
