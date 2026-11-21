namespace Backend.Features.Reports;

// Where report PDFs live: outside the database, which keeps only the storage key (PROJECT.md 20.1). Reports:Path is
// resolved against the content root; the pod mounts a volume there so reports survive restarts.
public sealed class ReportStorage(IConfiguration configuration, IHostEnvironment environment, ILogger<ReportStorage> logger)
{
    public string Root { get; } = Path.GetFullPath(configuration["Reports:Path"] ?? "reports", environment.ContentRootPath);

    // Keys are generated here ("{report id}.pdf"), never taken from a request.
    public string PathFor(string storageKey) => Path.Combine(Root, storageKey);

    // Removes a file whose row was never saved. Never throws, so the caller's own error or answer is kept; a file that
    // cannot be removed is logged so it can be cleaned up by hand.
    public void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not remove unsaved report file {Path}", path);
        }
    }
}
