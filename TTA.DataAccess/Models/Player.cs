using TTA.DataAccess.Enums;
using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class Player : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public Guid HomeClubId { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public DateTime BirthDate { get; set; }
    public Gender Gender { get; set; }
    public DateTime CreatedAt { get; set; }
}