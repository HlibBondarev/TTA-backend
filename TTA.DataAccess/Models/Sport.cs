using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class Sport : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public Guid? DefaultConfigId { get; set; }
}