using TTA.DataAccess.Enums;

namespace TTA.DataAccess.Models.Auth;

public class AccessPolicy
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public AppRole Role { get; set; }
    public TargetScope TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
