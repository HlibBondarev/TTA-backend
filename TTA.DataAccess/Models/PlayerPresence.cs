using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class PlayerPresence : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public int PeriodNumber { get; set; }
    public DateTime TimeIn { get; set; }
    public DateTime? TimeOut { get; set; }
}
