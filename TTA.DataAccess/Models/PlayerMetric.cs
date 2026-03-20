using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class PlayerMetric : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public double? Weight { get; set; }
    public double? Height { get; set; }
    public DateTime MeasuredAt { get; set; }
}
