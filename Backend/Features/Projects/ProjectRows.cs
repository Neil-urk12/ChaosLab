using System.Linq.Expressions;
using Backend.Domain.Entities;
using Backend.Domain.Enums;

namespace Backend.Features.Projects;

// List rows shared by the workspace overview and the project detail queries.
public sealed record TestRunRow(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    string EnvironmentName,
    TestRunStatus Status,
    int CompletedScenarios,
    int TotalScenarios,
    string RequestedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    decimal? Score,
    int FailedFindings)
{
    public static readonly Expression<Func<TestRun, TestRunRow>> FromRun = run => new TestRunRow(
        run.Id, run.ProjectId, run.Project.Name, run.Environment.Name, run.Status,
        run.CompletedScenarios, run.TotalScenarios, run.RequestedBy.DisplayName,
        run.CreatedAt, run.StartedAt, run.CompletedAt, run.ResilienceScore,
        run.ResilienceFindings.Count(finding => finding.Outcome == ResilienceFindingOutcome.Failed));
}

public sealed record FindingRow(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    string HttpMethod,
    string Path,
    ResiliencePattern Pattern,
    string Summary,
    DateTimeOffset CreatedAt)
{
    public static readonly Expression<Func<ResilienceFinding, FindingRow>> FromFinding = finding => new FindingRow(
        finding.Id, finding.TestRun.ProjectId, finding.TestRun.Project.Name, finding.Endpoint.HttpMethod,
        finding.Endpoint.Path, finding.Pattern, finding.Summary, finding.CreatedAt);
}
