namespace TTA.BusinessLogic.Services;

/// <summary>
/// Contains names of the standard identity resource claims types.
/// </summary>
public static class IdentityResourceClaimsTypes
{
    /// <summary>
    /// Represents the unique identifier for the subject (user).
    /// </summary>
    public const string Sub = "sub";

    /// <summary>
    /// Represents the role assigned to the user.
    /// </summary>
    public const string Role = "role";

    /// <summary>
    /// Represents the email address of the user.
    /// </summary>
    public const string Email = "email";
}
