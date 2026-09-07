using System.ComponentModel.DataAnnotations;

namespace ChatApp.DTOs;

public record UpdateProfileRequest(
    [Required, MaxLength(100)] string FullName,
    [MaxLength(255)] string? Bio,
    string? AvatarUrl
);