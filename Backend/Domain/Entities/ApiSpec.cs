
using System.Text.Json;
using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class ApiSpec
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public Guid UploadedById { get; set; }
    public User UploadedBy { get; set; } = null!;
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
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ApiEndpoint> Endpoints { get; set; } = [];
}
