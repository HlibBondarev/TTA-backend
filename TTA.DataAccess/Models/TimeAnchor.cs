using TTA.DataAccess.Enums;
using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class TimeAnchor : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public int PeriodNumber { get; set; }
    public TimeAnchorType Type { get; set; }
    public DateTime Timestamp { get; set; }
}