using TTA.Common.Enums;
using TTA.DataAccess.Models.Auth;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository.Auth;

public interface IAccessRepository : IEntityRepositoryBase<Guid, AccessPolicy>
{
    Task<AppRole?> GetUserRoleForScope(string userId, TargetScope scope, Guid? resourceId);
}