using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Backend.Domain.Entities;

[Index(nameof(IsDeleted))]
public class User : IdentityUser<Guid>
{
    // Table properties
    [Required]
    [StringLength(100)]
    public string Firstname { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Lastname { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }

    // Metadata
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; set; } = false;
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigation properties
    public ICollection<Project> CreatedProjects { get; set; } = [];
    public ICollection<ProjectMember> ProjectMemberships { get; set; } = [];
}
