using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class MatchLineup : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public int Number { get; set; }
    public bool IsInStartingLineup { get; set; }
    public Guid PositionId { get; set; }
}