
namespace Backend.Domain.Entities;

public class Report
{
    // Keys
    public Guid Id { get; set; }
    public Guid TestRunId { get; set; }

    // Table properties
    public int Version { get; set; } = 1;
    public string Format { get; set; } = "pdf";
    public string FileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;

    // Metadata
    public Guid? GeneratedById { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public TestRun TestRun { get; set; } = null!;
    public User? GeneratedBy { get; set; }
}
