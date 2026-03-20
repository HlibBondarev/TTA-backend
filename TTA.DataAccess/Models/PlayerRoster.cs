using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class PlayerRoster : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public Guid TournamentId { get; set; }
    public Guid TeamId { get; set; }
    public int Number { get; set; }
    public Guid PositionId { get; set; }
}
