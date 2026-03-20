using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class User : IKeyedEntity<string>
{
    // Auth0 Identity ID
    public string Id { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}