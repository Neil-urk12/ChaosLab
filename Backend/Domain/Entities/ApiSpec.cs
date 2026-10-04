
using System.Text.Json;
using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class ApiSpec
{
    // Keys
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }

    // Table properties
    public string? Title { get; set; }
    public string? VersionLabel { get; set; }
    public string OpenApiVersion { get; set; } = string.Empty;
    public string? SourceFilename { get; set; }
    public ApiSpecSourceType SourceType { get; set; }
    public string? SourceUrl { get; set; }
    public ApiSpecFormat SourceFormat { get; set; }
    public string OriginalDocument { get; set; } = string.Empty;
    public JsonDocument ParsedDocument { get; set; } = null!;
    public string? SpecHash { get; set; }
    // The project's single current specification; inactive versions remain available to historical runs.
    public bool IsActive { get; set; } = true;

    // Metadata
    public Guid UploadedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Project Project { get; set; } = null!;
    public User UploadedBy { get; set; } = null!;
    public ICollection<ApiEndpoint> Endpoints { get; set; } = [];
}
