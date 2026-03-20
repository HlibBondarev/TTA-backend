using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class Tournament : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public Guid SportId { get; set; }
    public Guid ConfigurationId { get; set; }
    public string Name { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime CreatedAt { get; set; }
}
