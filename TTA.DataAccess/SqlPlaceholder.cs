using System.Diagnostics.CodeAnalysis;

namespace TTA.DataAccess;

/// <summary>
/// Marker class used by DbUp to locate the assembly containing SQL scripts.
/// </summary>
[ExcludeFromCodeCoverage]
public class SqlPlaceholder
{
    // This class is intentionally left empty.
    private SqlPlaceholder()
    {
    }
}