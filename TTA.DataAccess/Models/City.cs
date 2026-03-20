using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class City : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public int RegionId { get; set; }
    public string Name { get; set; } = null!;
}