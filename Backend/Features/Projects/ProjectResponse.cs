namespace Backend.Features.Projects;

public sealed record ProjectResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
