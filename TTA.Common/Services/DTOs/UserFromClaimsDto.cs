using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TTA.Common.Services.DTOs;

/// <summary>
/// Data transfer object representing user information retrieved from identity claims.
/// </summary>
/// <param name="Id">Unique identifier (Subject) of the user.</param>
/// <param name="Email">The user's email address.</param>
/// <param name="Name">The user's display name.</param>
public record UserFromClaimsDto(
    [Required]
    [MaxLength(64)]
    [property: JsonPropertyName("sub")]
    string Id,

    [Required]
    [EmailAddress]
    string Email,

    [Required]
    [StringLength(20, MinimumLength = 3)]
    string Name
);
