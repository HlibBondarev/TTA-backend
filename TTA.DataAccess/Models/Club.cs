using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class Club : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public Guid CityId { get; set; }
    public string Name { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
