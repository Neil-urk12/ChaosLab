using Backend.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Backend.Infrastructure.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<User, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<ApiSpec> ApiSpecs => Set<ApiSpec>();
    public DbSet<ApiEndpoint> ApiEndpoints => Set<ApiEndpoint>();
    public DbSet<ApiEnvironment> ApiEnvironments => Set<ApiEnvironment>();
    public DbSet<AuthProfile> AuthProfiles => Set<AuthProfile>();
    public DbSet<EnvironmentHeader> EnvironmentHeaders => Set<EnvironmentHeader>();
    public DbSet<ChaosScenario> ChaosScenarios => Set<ChaosScenario>();
    public DbSet<TestRun> TestRuns => Set<TestRun>();
    public DbSet<ScenarioExecution> ScenarioExecutions => Set<ScenarioExecution>();
    public DbSet<HttpObservation> HttpObservations => Set<HttpObservation>();
    public DbSet<EndpointResult> EndpointResults => Set<EndpointResult>();
    public DbSet<ResilienceFinding> ResilienceFindings => Set<ResilienceFinding>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<Report> Reports => Set<Report>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Catalog references restrict deletion so historical runs survive archiving or regeneration.
        // Only run-owned data cascades; links between those dependents use NO ACTION so a
        // single TestRun deletion can cascade through all of them without sibling delete conflicts.
        builder.Entity<Project>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.HasOne(x => x.CreatedBy).WithMany(x => x.CreatedProjects)
                .HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.CreatedAt);
        });

        builder.Entity<ProjectMember>(entity =>
        {
            // RemovedAt marks inactive membership. Reactivate the same row when re-adding a user.
            // Do not globally filter removed rows: membership management must be able to find them.
            entity.HasKey(x => new { x.ProjectId, x.UserId });
            entity.HasOne(x => x.Project).WithMany(x => x.Members)
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.User).WithMany(x => x.ProjectMemberships)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RemovedBy).WithMany()
                .HasForeignKey(x => x.RemovedById).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ApiSpec>(entity =>
        {
            entity.HasAlternateKey(x => new { x.Id, x.ProjectId });
            entity.HasOne(x => x.Project).WithMany(x => x.ApiSpecs)
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.UploadedBy).WithMany()
                .HasForeignKey(x => x.UploadedById).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.ParsedDocument).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.ProjectId, x.CreatedAt });
            entity.HasIndex(x => x.ProjectId).IsUnique().HasFilter("\"IsActive\" = TRUE");
        });

        builder.Entity<ApiEndpoint>(entity =>
        {
            entity.Property(x => x.Path).HasMaxLength(1024);
            entity.Property(x => x.HttpMethod).HasMaxLength(16);
            entity.Property(x => x.OperationId).HasMaxLength(256);
            entity.HasOne(x => x.ApiSpec).WithMany(x => x.Endpoints)
                .HasForeignKey(x => x.ApiSpecId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ApiSpecId, x.Path, x.HttpMethod }).IsUnique();
            entity.Property(x => x.Tags).HasColumnType("jsonb");
            entity.Property(x => x.Parameters).HasColumnType("jsonb");
            entity.Property(x => x.RequestBodySchema).HasColumnType("jsonb");
            entity.Property(x => x.Responses).HasColumnType("jsonb");
            entity.Property(x => x.SecurityRequirements).HasColumnType("jsonb");
        });

        builder.Entity<ApiEnvironment>(entity =>
        {
            entity.HasAlternateKey(x => new { x.Id, x.ProjectId });
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.BaseUrl).HasMaxLength(2048);
            entity.HasOne(x => x.Project).WithMany(x => x.Environments)
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CreatedBy).WithMany()
                .HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AuthorizationConfirmedBy).WithMany()
                .HasForeignKey(x => x.AuthorizationConfirmedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
            entity.HasIndex(x => x.ProjectId).IsUnique().HasFilter("\"IsDefault\" = TRUE");
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_ApiEnvironments_RequestTimeoutSeconds", "\"RequestTimeoutSeconds\" BETWEEN 1 AND 300"));
        });

        builder.Entity<EnvironmentHeader>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Value).HasMaxLength(4096);
            entity.Property(x => x.Hint).HasMaxLength(4);
            // Headers are configuration, not run history, so they go with their environment.
            entity.HasOne(x => x.Environment).WithMany(x => x.Headers)
                .HasForeignKey(x => x.EnvironmentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.EnvironmentId, x.Name }).IsUnique();
            entity.Property(x => x.AppliesTo).HasColumnType("jsonb");
        });

        builder.Entity<AuthProfile>(entity =>
        {
            entity.HasAlternateKey(x => new { x.Id, x.EnvironmentId });
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.HasOne(x => x.Environment).WithMany(x => x.AuthProfiles)
                .HasForeignKey(x => x.EnvironmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.EnvironmentId, x.Name }).IsUnique();
            entity.Property(x => x.PublicConfig).HasColumnType("jsonb");
        });

        builder.Entity<ChaosScenario>(entity =>
        {
            entity.HasAlternateKey(x => new { x.Id, x.EndpointId });
            entity.HasOne(x => x.Endpoint).WithMany(x => x.ChaosScenarios)
                .HasForeignKey(x => x.EndpointId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Parameters).HasColumnType("jsonb");
            entity.Property(x => x.Invariants).HasColumnType("jsonb");
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.HasIndex(x => new { x.EndpointId, x.CreatedAt });
            // The generator makes at most one scenario per fault type per endpoint.
            entity.HasIndex(x => new { x.EndpointId, x.ScenarioType }).IsUnique();
        });

        builder.Entity<TestRun>(entity =>
        {
            entity.HasOne(x => x.Project).WithMany(x => x.TestRuns)
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ApiSpec).WithMany()
                .HasForeignKey(x => new { x.ApiSpecId, x.ProjectId })
                .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Environment).WithMany(x => x.TestRuns)
                .HasForeignKey(x => new { x.EnvironmentId, x.ProjectId })
                .HasPrincipalKey(x => new { x.Id, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AuthProfile).WithMany()
                .HasForeignKey(x => new { x.AuthProfileId, x.EnvironmentId })
                .HasPrincipalKey(x => new { x.Id, x.EnvironmentId })
                .IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RequestedBy).WithMany()
                .HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.RunConfig).HasColumnType("jsonb");
            entity.Property(x => x.ScoringVersion).HasMaxLength(32);
            entity.Property(x => x.ResilienceScore).HasPrecision(5, 2);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_TestRuns_ResilienceScore", "\"ResilienceScore\" BETWEEN 0 AND 100");
                table.HasCheckConstraint("CK_TestRuns_ScenarioCounts",
                    "\"TotalScenarios\" >= 0 AND \"CompletedScenarios\" >= 0 AND \"CompletedScenarios\" <= \"TotalScenarios\"");
            });
            entity.HasIndex(x => new { x.ProjectId, x.CreatedAt });
            entity.HasIndex(x => new { x.Status, x.CreatedAt });
        });

        builder.Entity<ScenarioExecution>(entity =>
        {
            entity.HasOne(x => x.TestRun).WithMany(x => x.ScenarioExecutions)
                .HasForeignKey(x => x.TestRunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Endpoint).WithMany(x => x.ScenarioExecutions)
                .HasForeignKey(x => x.EndpointId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Scenario).WithMany(x => x.ScenarioExecutions)
                .HasForeignKey(x => new { x.ScenarioId, x.EndpointId })
                .HasPrincipalKey(x => new { x.Id, x.EndpointId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.TestRunId, x.SequenceNo }).IsUnique();
            entity.Property(x => x.Parameters).HasColumnType("jsonb");
            entity.Property(x => x.Invariants).HasColumnType("jsonb");
            entity.Property(x => x.Score).HasPrecision(5, 2);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_ScenarioExecutions_Score", "\"Score\" BETWEEN 0 AND 100");
                table.HasCheckConstraint("CK_ScenarioExecutions_SequenceNo", "\"SequenceNo\" >= 1");
            });
        });

        builder.Entity<HttpObservation>(entity =>
        {
            entity.HasOne(x => x.ScenarioExecution).WithMany(x => x.Observations)
                .HasForeignKey(x => x.ScenarioExecutionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ScenarioExecutionId, x.AttemptNo });
            entity.Property(x => x.Metadata).HasColumnType("jsonb");
            entity.ToTable(table => table.HasCheckConstraint("CK_HttpObservations_AttemptNo", "\"AttemptNo\" >= 1"));
        });

        builder.Entity<EndpointResult>(entity =>
        {
            entity.HasOne(x => x.TestRun).WithMany(x => x.EndpointResults)
                .HasForeignKey(x => x.TestRunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Endpoint).WithMany(x => x.Results)
                .HasForeignKey(x => x.EndpointId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.TestRunId, x.EndpointId }).IsUnique();
            entity.Property(x => x.Evidence).HasColumnType("jsonb");
            entity.Property(x => x.RetryScore).HasPrecision(5, 2);
            entity.Property(x => x.TimeoutScore).HasPrecision(5, 2);
            entity.Property(x => x.CircuitBreakerScore).HasPrecision(5, 2);
            entity.Property(x => x.FallbackScore).HasPrecision(5, 2);
            entity.Property(x => x.IdempotencyScore).HasPrecision(5, 2);
            entity.Property(x => x.OverallScore).HasPrecision(5, 2);
            entity.ToTable(table => table.HasCheckConstraint("CK_EndpointResults_Scores",
                "\"RetryScore\" BETWEEN 0 AND 100 AND \"TimeoutScore\" BETWEEN 0 AND 100 " +
                "AND \"CircuitBreakerScore\" BETWEEN 0 AND 100 AND \"FallbackScore\" BETWEEN 0 AND 100 " +
                "AND \"IdempotencyScore\" BETWEEN 0 AND 100 AND \"OverallScore\" BETWEEN 0 AND 100"));
        });

        builder.Entity<ResilienceFinding>(entity =>
        {
            entity.HasOne(x => x.TestRun).WithMany(x => x.ResilienceFindings)
                .HasForeignKey(x => x.TestRunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Endpoint).WithMany(x => x.ResilienceFindings)
                .HasForeignKey(x => x.EndpointId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ScenarioExecution).WithMany()
                .HasForeignKey(x => x.ScenarioExecutionId).OnDelete(DeleteBehavior.NoAction);
            entity.Property(x => x.Evidence).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.TestRunId, x.CreatedAt });
            entity.HasIndex(x => x.EndpointId);
            entity.HasIndex(x => x.ScenarioExecutionId);
        });

        builder.Entity<Recommendation>(entity =>
        {
            entity.Property(x => x.Category).HasMaxLength(100);
            entity.Property(x => x.Title).HasMaxLength(500);
            entity.HasOne(x => x.TestRun).WithMany(x => x.Recommendations)
                .HasForeignKey(x => x.TestRunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Endpoint).WithMany()
                .HasForeignKey(x => x.EndpointId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ScenarioExecution).WithMany()
                .HasForeignKey(x => x.ScenarioExecutionId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.ResilienceFinding).WithMany()
                .HasForeignKey(x => x.ResilienceFindingId).OnDelete(DeleteBehavior.NoAction);
            entity.Property(x => x.Evidence).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.TestRunId, x.CreatedAt });
            entity.HasIndex(x => x.ResilienceFindingId);
        });

        builder.Entity<Report>(entity =>
        {
            entity.Property(x => x.Format).HasMaxLength(32);
            entity.Property(x => x.FileName).HasMaxLength(512);
            entity.HasOne(x => x.TestRun).WithMany(x => x.Reports)
                .HasForeignKey(x => x.TestRunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.GeneratedBy).WithMany()
                .HasForeignKey(x => x.GeneratedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.TestRunId, x.Version }).IsUnique();
            entity.ToTable(table => table.HasCheckConstraint("CK_Reports_Version", "\"Version\" >= 1"));
        });
    }
}
