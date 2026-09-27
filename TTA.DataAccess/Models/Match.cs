using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class Match : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public Guid HomeTeamId { get; set; }
    public Guid GuestTeamId { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string? MatchNumber { get; set; }
    public string? Venue { get; set; }
    public double? Temperature { get; set; }
    public int? HomeScore { get; set; }
    public int? GuestScore { get; set; }
    public DateTime CreatedAt { get; set; }
}
