using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class PlayerPositionDefinition : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public Guid SportId { get; set; }
    public string Name { get; set; } = null!;
    public string ShortName { get; set; } = null!;
}