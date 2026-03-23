using TTA.DataAccess.Models.Auth;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Auth;

public interface IAccessRepository : IEntityRepositoryBase<Guid, AccessPolicy>
{
    Task<string?> GetUserRoleForScope(string userId, string targetType, Guid? targetId);
}