using System.Text.Json;
using System.Text.Json.Serialization;

namespace TTA.Common.Extensions;

/// <summary>
/// Provides extension methods for configuring <see cref="JsonSerializerOptions"/>.
/// </summary>
public static class JsonSerializerOptionsExtensions
{
    /// <summary>
    /// Configures the provided <see cref="JsonSerializerOptions"/> with default application settings.
    /// </summary>
    /// <param name="jsonSerializerOptions">The options instance to configure.</param>
    /// <returns>The configured <see cref="JsonSerializerOptions"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the options instance is null.</exception>
    public static JsonSerializerOptions GetDefault(this JsonSerializerOptions jsonSerializerOptions)
    {
        ArgumentNullException.ThrowIfNull(jsonSerializerOptions);

        jsonSerializerOptions.PropertyNameCaseInsensitive = true;
        jsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        jsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;

        return jsonSerializerOptions;
    }
}
