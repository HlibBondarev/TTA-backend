using System.ComponentModel.DataAnnotations.Schema;
using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

[Table("Countries")]
public class Country : IKeyedEntity<int>
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty; // ISO 3166-1 alpha-3 (e.g., UKR)
    public DateTime CreatedAt { get; set; }
}