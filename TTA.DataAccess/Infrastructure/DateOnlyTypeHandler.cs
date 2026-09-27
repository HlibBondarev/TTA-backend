using Dapper;
using System.Data;

namespace TTA.DataAccess.Infrastructure;

/// <summary>
/// Provides a custom Dapper type handler for mapping PostgreSQL <c>DATE</c> columns 
/// to .NET <see cref="DateOnly"/> properties.
/// </summary>
public class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    /// <summary>
    /// Configures the database parameter value before executing a command.
    /// </summary>
    /// <param name="parameter">The database parameter to be configured.</param>
    /// <param name="value">The <see cref="DateOnly"/> value to be stored in the database.</param>
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.Value = value;
    }

    /// <summary>
    /// Converts the database value (typically <see cref="DateOnly"/> or <see cref="DateTime"/>) 
    /// back into a .NET <see cref="DateOnly"/> object.
    /// </summary>
    /// <param name="value">The raw value returned from the database driver.</param>
    /// <returns>A validated <see cref="DateOnly"/> instance.</returns>
    /// <exception cref="InvalidCastException">
    /// Thrown when the database value is not a compatible type (neither DateOnly nor DateTime).
    /// </exception>
    public override DateOnly Parse(object value)
    {
        // If Npgsql already returned DateOnly (modern driver behavior), just return it
        if (value is DateOnly dateOnly)
        {
            return dateOnly;
        }

        // If it's a DateTime (standard ADO.NET or older driver behavior), convert it to DateOnly
        if (value is DateTime dateTime)
        {
            return DateOnly.FromDateTime(dateTime);
        }

        throw new InvalidCastException($"Unable to cast {value.GetType().Name} to DateOnly. Expected DateOnly or DateTime.");
    }
}