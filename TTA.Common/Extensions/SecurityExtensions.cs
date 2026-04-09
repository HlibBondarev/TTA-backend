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
}