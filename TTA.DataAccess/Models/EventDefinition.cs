using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class EventDefinition : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public Guid SportId { get; set; }
    public string Name { get; set; } = null!;
    public string ShortName { get; set; } = null!;
    public bool IsPositive { get; set; }
    public DateTime CreatedAt { get; set; }
}