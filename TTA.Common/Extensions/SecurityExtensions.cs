namespace TTA.Common.Extensions;

public static class SecurityExtensions
{
    /// <summary>
    /// Masks an email address to protect PII in logs.
    /// Example: test-user@example.com -> te***@example.com
    /// </summary>
    public static string MaskEmail(this string email)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
            return "****";

        var parts = email.Split('@');
        var name = parts[0];
        var domain = parts[1];

        if (name.Length <= 2)
            return $"***@{domain}";

        return $"{name[..2]}***@{domain}";
    }

    /// <summary>
    /// Masks a user Auth Id to protect PII in logs.
    /// Handles standard formats like "provider|id" and falls back safely for other formats.
    /// Example: auth0|69cf7ec5... -> auth0|69c***
    /// </summary>
    /// <param name="authId">The raw authentication identifier.</param>
    /// <returns>A masked version of the identifier.</returns>
    public static string MaskAuthId(this string authId)
    {
        if (string.IsNullOrEmpty(authId))
            return "***";

        var parts = authId.Split('|');

        // Case 1: Standard provider|id format (e.g., auth0|12345)
        if (parts.Length > 1)
        {
            var prefix = parts[0];
            var identity = parts[1];

            if (identity.Length <= 3)
                return $"{prefix}|***";

            return $"{prefix}|{identity[..3]}***";
        }

        // Case 2: Non-standard format (no '|' separator)
        // Mask the string but keep the first 3 characters if possible
        return authId.Length <= 3 ? "***" : $"{authId[..3]}***";
    }
}