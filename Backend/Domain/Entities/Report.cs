
namespace Backend.Domain.Entities;

public class Report
{
    public Guid Id { get; set; }
    public Guid TestRunId { get; set; }
    public TestRun TestRun { get; set; } = null!;
    public int Version { get; set; } = 1;
    public string Format { get; set; } = "pdf";
    public string FileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public Guid? GeneratedById { get; set; }
    public User? GeneratedBy { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}
