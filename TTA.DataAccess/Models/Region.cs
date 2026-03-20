using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class Region : IKeyedEntity<int>
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
}
