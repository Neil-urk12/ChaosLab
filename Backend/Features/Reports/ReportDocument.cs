using System.Globalization;
using System.Text.Json;
using Backend.Domain.Enums;
using Backend.Features.Findings;
using Backend.Features.TestRuns;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Backend.Features.Reports;

// Everything a report shows (PROJECT.md 15.4). Built only from stored results and the run's configuration snapshot,
// never from request headers, bodies or credentials, so nothing secret can reach the PDF.
public sealed record ReportData(
    string ProjectName,
    string? SpecTitle,
    string? SpecVersion,
    string OpenApiVersion,
    GetRun.Response Run,
    JsonElement RunConfig,
    List<ListFindings.Row> Findings,
    // Skipped and runner-failed executions grouped by reason, most frequent first.
    List<ReportData.ExecutionProblem> ExecutionProblems,
    string GeneratedBy,
    DateTimeOffset GeneratedAt)
{
    public sealed record ExecutionProblem(ScenarioExecutionStatus Status, string? Reason, int Count);
}

// The PDF layout. The only file that knows QuestPDF, so the library can be swapped here alone.
public static class ReportDocument
{
    private const string Muted = "#64748B";
    private const string Line = "#CBD5E1";

    // Faults that only change what the runner receives (FeaturesFlow 2.16), so their findings are limited.
    private static readonly ScenarioType[] RunnerOnlyFaults =
        [ScenarioType.ServerErrorStorm, ScenarioType.Throttle429, ScenarioType.MalformedJson, ScenarioType.EmptyResponse];

    // The Community license covers individuals, academic and open-source use, and organisations under USD 1M annual
    // revenue (questpdf.com/license). Anyone else who deploys ChaosLab needs their own QuestPDF license.
    static ReportDocument() => QuestPDF.Settings.License = LicenseType.Community;

    public static string RunLabel(int number) => $"RUN-{number:000}";

    public static byte[] Render(ReportData data) => Document.Create(document => document.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(40);
        // Lato ships inside the QuestPDF package, so the PDF looks the same in a container without system fonts.
        page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(9).LineHeight(1.3f));
        page.Header().PaddingBottom(8).Row(row =>
        {
            row.RelativeItem().Text($"ChaosLab resilience report · {data.ProjectName}").FontColor(Muted);
            row.AutoItem().Text(RunLabel(data.Run.Number)).FontColor(Muted);
        });
        page.Content().Column(column =>
        {
            column.Spacing(16);
            Cover(column, data);
            Section(column, "Score", section => Scores(section, data));
            Section(column, "Test configuration", section => Configuration(section, data));
            if (data.Run.Endpoints.Count > 0)
                Section(column, "Endpoint scores", section => Endpoints(section, data));
            Section(column, "Findings", section => FindingsSection(section, data));
            if (data.Run.Timelines.Count > 0)
                Section(column, "Failure timelines", section => Timelines(section, data));
            Section(column, "Warnings and limitations", section => Limitations(section, data));
        });
        page.Footer().AlignCenter().Text(text =>
        {
            text.DefaultTextStyle(style => style.FontColor(Muted));
            text.Span("Page ");
            text.CurrentPageNumber();
            text.Span(" of ");
            text.TotalPages();
        });
    })).GeneratePdf();

    private static void Cover(ColumnDescriptor column, ReportData data)
    {
        var run = data.Run;
        column.Item().Column(cover =>
        {
            cover.Spacing(4);
            cover.Item().Text($"{data.ProjectName}: {RunLabel(run.Number)}").FontSize(20).Bold();
            cover.Item().Text($"Generated {Date(data.GeneratedAt)} by {data.GeneratedBy}").FontColor(Muted);
        });
        column.Item().Element(Facts(
            ("Status", StatusLabel(run.Status)),
            ("Environment", run.EnvironmentName),
            ("Specification", string.Join(" · ", new[] { data.SpecTitle, data.SpecVersion, $"OpenAPI {data.OpenApiVersion}" }
                .Where(part => !string.IsNullOrWhiteSpace(part)))),
            ("Requested by", run.RequestedBy),
            ("Started", run.StartedAt is { } started ? Date(started) : "Not started"),
            ("Finished", run.CompletedAt is { } completed ? Date(completed) : "–"),
            ("Scenarios", $"{run.CompletedScenarios} of {run.TotalScenarios} finished, {run.SkippedScenarios} of them skipped"),
            ("Scoring version", run.ScoringVersion)));
    }

    private static void Scores(ColumnDescriptor column, ReportData data)
    {
        var run = data.Run;
        column.Item().Row(row =>
        {
            row.Spacing(24);
            row.AutoItem().Column(score =>
            {
                score.Item().Text(run.Score is { } value ? Number(value) : "Not scored").FontSize(28).Bold();
                score.Item().Text("Resilience score (0–100)").FontColor(Muted);
            });
            row.AutoItem().Column(coverage =>
            {
                coverage.Item().Text(run.EvidenceCoverage is { } value ? $"{Number(value)}%" : "–").FontSize(28).Bold();
                coverage.Item().Text("Evidence coverage").FontColor(Muted);
            });
            if (run.Previous is { } previous && run.Score is { } current)
                row.AutoItem().Column(change =>
                {
                    var delta = current - previous.Score;
                    change.Item().Text($"{(delta > 0 ? "+" : "")}{Number(delta)}").FontSize(28).Bold();
                    change.Item().Text($"Since {RunLabel(previous.Number)} ({Number(previous.Score)})").FontColor(Muted);
                });
        });

        var counts = data.Findings.GroupBy(finding => finding.Outcome).ToDictionary(group => group.Key, group => group.Count());
        column.Item().Text(string.Join("   ", Enum.GetValues<ResilienceFindingOutcome>()
            .OrderBy(OutcomeRank)
            .Select(outcome => $"{OutcomeLabel(outcome)}: {counts.GetValueOrDefault(outcome)}")));

        if (run.Categories is not { Count: > 0 } categories)
            return;
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3);
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
            });
            HeaderRow(table, "Category", "Weight", "Score", "Scored findings", "Inconclusive");
            foreach (var category in categories)
                Cells(table, CategoryLabel(category.Category), category.Weight.ToString(CultureInfo.InvariantCulture),
                    category.Score is { } score ? Number(score) : "Not scored", category.Findings.ToString(CultureInfo.InvariantCulture),
                    category.Inconclusive.ToString(CultureInfo.InvariantCulture));
        });
    }

    private static void Configuration(ColumnDescriptor column, ReportData data)
    {
        var config = data.RunConfig;
        var target = config.ValueKind == JsonValueKind.Object && config.TryGetProperty("target", out var value) ? value : default;
        column.Item().Element(Facts(
            ("Target", Text(target, "baseUrl") ?? "–"),
            ("Environment kind", Text(target, "kind") ?? "–"),
            ("Request timeout", Text(target, "requestTimeoutSeconds") is { } timeout ? $"{timeout} s" : "–"),
            ("TLS verification", Text(target, "verifyTls") switch { "true" => "On", "false" => "Off", _ => "–" }),
            ("Concurrency", Text(config, "concurrency") ?? "–"),
            ("Baseline requests", Text(config, "baselineRequests") switch { "true" => "Yes", "false" => "No", _ => "–" }),
            ("Runner retries", Text(config, "runnerRetries") ?? "–"),
            ("State-changing scenarios", Text(config, "stateChangingScenarios") is { } count
                ? $"{count}{(Text(config, "stateChangingConfirmed") == "true" ? ", confirmed before the run" : "")}"
                : "–")));
    }

    private static void Endpoints(ColumnDescriptor column, ReportData data)
    {
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(60);
                columns.RelativeColumn();
                columns.ConstantColumn(60);
            });
            HeaderRow(table, "Method", "Path", "Score");
            foreach (var endpoint in data.Run.Endpoints)
                Cells(table, endpoint.HttpMethod, endpoint.Path, Number(endpoint.Score));
        });
    }

    private static void FindingsSection(ColumnDescriptor column, ReportData data)
    {
        if (data.Findings.Count == 0)
        {
            column.Item().Text("This run produced no findings.").FontColor(Muted);
            return;
        }
        foreach (var finding in data.Findings
                     .OrderBy(finding => OutcomeRank(finding.Outcome)).ThenByDescending(finding => finding.Severity))
            // EnsureSpace keeps a finding's heading off the bottom of a page; a long finding still breaks across pages.
            column.Item().EnsureSpace(60).BorderLeft(2).BorderColor(Line).PaddingLeft(8).Column(item =>
            {
                item.Spacing(3);
                item.Item().Text($"{OutcomeLabel(finding.Outcome)} · {finding.Severity} · {finding.HttpMethod} {finding.Path}").Bold();
                MutedLine(item, string.Join(" · ", new[]
                {
                    finding.ScenarioType is { } type ? FaultLabel(type) : null,
                    finding.Category is { } category ? CategoryLabel(category) : null
                }.Where(part => part is not null)));
                item.Item().Text(finding.Summary);
                var evidence = finding.Evidence;
                Labelled(item, "Expected", Text(evidence, "expected"));
                Labelled(item, "Observed", Text(evidence, "observed"));
                if (evidence.ValueKind == JsonValueKind.Object && evidence.TryGetProperty("interpretation", out var interpretation))
                {
                    Labelled(item, "Suggests", Text(interpretation, "suggests"));
                    Labelled(item, "Not proven", Text(interpretation, "notProven"));
                }
                if (evidence.ValueKind == JsonValueKind.Object && evidence.TryGetProperty("facts", out var facts)
                    && facts.ValueKind == JsonValueKind.Array)
                    foreach (var fact in facts.EnumerateArray())
                        MutedLine(item, $"{Text(fact, "label")}: {Text(fact, "value")}");
                if (finding.Recommendation is { } recommendation)
                    item.Item().PaddingTop(2).Text(text =>
                    {
                        text.Span("Recommendation: ").Bold();
                        text.Span($"{recommendation.Title}. {recommendation.Text}");
                    });
            });
    }

    private static void Timelines(ColumnDescriptor column, ReportData data)
    {
        foreach (var timeline in data.Run.Timelines)
            column.Item().EnsureSpace(80).Column(item =>
            {
                item.Spacing(4);
                item.Item().Text($"{timeline.HttpMethod} {timeline.Path} · {FaultLabel(timeline.ScenarioType)}").Bold();
                item.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(44);
                        columns.ConstantColumn(76);
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(2);
                    });
                    HeaderRow(table, "Attempt", "Time", "Request", "Status", "Outcome");
                    foreach (var e in timeline.Events)
                        Cells(table, e.AttemptNo.ToString(CultureInfo.InvariantCulture),
                            e.StartedAt.UtcDateTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                            $"{e.RequestMethod} {e.RequestPath}",
                            Status(e), $"{Words(e.OutcomeType.ToString())}{(e.DurationMs is { } ms ? $" · {ms} ms" : "")}");
                });
            });
    }

    private static void Limitations(ColumnDescriptor column, ReportData data)
    {
        var run = data.Run;
        var notes = new List<string>();
        if (run.Score is null)
            notes.Add(run.Status switch
            {
                TestRunStatus.Completed => "This run finished before scores were stored, so it has no score.",
                TestRunStatus.PartiallyCompleted => "Not every scenario ran, so this run was not scored.",
                TestRunStatus.Cancelled => "This run was cancelled, so it was not scored. Evidence collected before the cancellation is included.",
                _ => "This run failed, so it was not scored."
            });
        if (!string.IsNullOrWhiteSpace(run.FailureMessage))
            notes.Add($"Run failure: {run.FailureMessage}");
        foreach (var problem in data.ExecutionProblems)
            notes.Add($"{problem.Count} scenario{(problem.Count == 1 ? " was" : "s were")} " +
                $"{(problem.Status == ScenarioExecutionStatus.Skipped ? "skipped" : "stopped by a runner error")}: " +
                $"{problem.Reason ?? "no reason recorded"}");
        var inconclusive = data.Findings.Count(finding => finding.Outcome == ResilienceFindingOutcome.Inconclusive);
        if (inconclusive > 0)
            notes.Add(inconclusive == 1
                ? "1 finding is Inconclusive: there was not enough observable evidence to judge it, so it is left out of the score."
                : $"{inconclusive} findings are Inconclusive: there was not enough observable evidence to judge them, so they are "
                    + "left out of the score.");
        if (data.Findings.Any(finding => finding.ScenarioType is { } type && RunnerOnlyFaults.Contains(type)))
            notes.Add("5xx storm, 429 throttle, malformed JSON and empty response faults change only what ChaosLab receives. "
                + "Their findings show whether the target's own answer broke, not how a client handles the fault.");
        if (data.Run.Timelines.Count == GetRun.MaxTimelines)
            notes.Add($"Only the {GetRun.MaxTimelines} most severe failure timelines are included.");
        notes.Add("The score covers only the scenarios executed in this run. Passing every scenario does not prove the API "
            + "has no resilience defects, and the score is not a reliability certification.");
        notes.Add("Findings describe behavior observed over HTTP. They do not identify the library or implementation behind it.");
        column.Item().Column(list =>
        {
            list.Spacing(4);
            foreach (var note in notes)
                list.Item().Text($"• {note}");
        });
    }

    // EnsureSpace moves a section that would split with less than 120 pt left, so a heading never ends a page alone.
    private static void Section(ColumnDescriptor column, string title, Action<ColumnDescriptor> content) =>
        column.Item().EnsureSpace(120).Column(section =>
        {
            section.Spacing(12);
            section.Item().PaddingTop(4).BorderBottom(1).BorderColor(Line).PaddingBottom(3).Text(title).FontSize(13).Bold();
            content(section);
        });

    private static Action<IContainer> Facts(params (string Label, string Value)[] facts) => container =>
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(130);
                columns.RelativeColumn();
            });
            foreach (var (label, value) in facts)
            {
                table.Cell().PaddingVertical(1).Text(label).FontColor(Muted);
                table.Cell().PaddingVertical(1).Text(value);
            }
        });

    private static void HeaderRow(TableDescriptor table, params string[] titles) => table.Header(header =>
    {
        foreach (var title in titles)
            header.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(3).Text(title).Bold();
    });

    private static void Cells(TableDescriptor table, params string[] values)
    {
        foreach (var value in values)
            table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(3).PaddingRight(4).Text(value);
    }

    private static void MutedLine(ColumnDescriptor column, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            column.Item().Text(value).FontColor(Muted);
    }

    // "ModifiedResponse" as "Modified response".
    private static string Words(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? $" {char.ToLowerInvariant(c)}" : c.ToString()));

    private static void Labelled(ColumnDescriptor column, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        column.Item().Text(text =>
        {
            text.Span($"{label}: ").SemiBold();
            text.Span(value);
        });
    }

    // A scalar property as text, or null when the element is not an object or lacks it.
    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
                _ => null
            }
            : null;

    private static string Status(GetRun.Event e) => e switch
    {
        { ClientStatusCode: { } client, UpstreamStatusCode: { } upstream } when client != upstream => $"{client} (target {upstream})",
        { ClientStatusCode: { } client } => client.ToString(CultureInfo.InvariantCulture),
        { UpstreamStatusCode: { } upstream } => upstream.ToString(CultureInfo.InvariantCulture),
        _ => e.ErrorType ?? "–"
    };

    private static string Number(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Date(DateTimeOffset value) =>
        value.UtcDateTime.ToString("d MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);

    // Worst first, the order the results page uses.
    private static int OutcomeRank(ResilienceFindingOutcome outcome) => outcome switch
    {
        ResilienceFindingOutcome.Failed => 0,
        ResilienceFindingOutcome.Degraded => 1,
        ResilienceFindingOutcome.Passed => 2,
        ResilienceFindingOutcome.Inconclusive => 3,
        _ => 4
    };

    // Labels match the frontend (src/lib/findings.ts, scenarios.ts, runs.ts).
    private static string OutcomeLabel(ResilienceFindingOutcome outcome) => outcome switch
    {
        ResilienceFindingOutcome.Passed => "Pass",
        ResilienceFindingOutcome.Failed => "Fail",
        ResilienceFindingOutcome.NotApplicable => "Not applicable",
        _ => outcome.ToString()
    };

    private static string StatusLabel(TestRunStatus status) => status switch
    {
        TestRunStatus.PartiallyCompleted => "Partially completed",
        _ => status.ToString()
    };

    private static string CategoryLabel(ScoringCategory category) => category switch
    {
        ScoringCategory.GracefulDegradation => "Graceful degradation",
        ScoringCategory.RateLimit => "Rate-limit",
        ScoringCategory.AuthenticationFailure => "Auth failure",
        ScoringCategory.ResponseValidation => "Response validation",
        ScoringCategory.DependencyFailure => "Dependency failure",
        _ => category.ToString()
    };

    private static string FaultLabel(ScenarioType type) => type switch
    {
        ScenarioType.ServerErrorStorm => "5xx storm",
        ScenarioType.Throttle429 => "429 throttle",
        ScenarioType.TimeoutDrop => "Timeout / drop",
        ScenarioType.MalformedJson => "Malformed JSON",
        ScenarioType.EmptyResponse => "Empty response",
        ScenarioType.SlowDrip => "Slow drip",
        ScenarioType.AuthExpiry => "Auth expiry",
        ScenarioType.DnsFailure => "Dependency failure",
        ScenarioType.DuplicateRequest => "Duplicate request",
        _ => type.ToString()
    };
}
