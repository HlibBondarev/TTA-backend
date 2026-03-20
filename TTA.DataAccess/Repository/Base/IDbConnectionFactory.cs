using System.Data;

namespace TTA.DataAccess.Repository.Base;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}
