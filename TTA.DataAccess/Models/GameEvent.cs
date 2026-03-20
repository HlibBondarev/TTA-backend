using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class GameEvent : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid? PlayerId { get; set; }
    public Guid EventDefinitionId { get; set; }
    public int PeriodNumber { get; set; }
    public DateTime EventTimestamp { get; set; }
    // Maps to INTERVAL in PostgreSQL
    public TimeSpan? NormalizedMatchTime { get; set; }
    // The new field we added to track direct impact on score
    public bool IsLeadToGoal { get; set; }
    public DateTime CreatedAt { get; set; }
}
