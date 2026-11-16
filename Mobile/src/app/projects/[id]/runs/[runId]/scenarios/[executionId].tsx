import { useLocalSearchParams } from 'expo-router';

import { outcomeLabels } from '@web/findings';
import { PLACEHOLDER, dateTime, duration } from '@web/format';
import { feedResult, feedResultStyles } from '@web/runs';
import { faultLabels } from '@web/scenarios';

import { useRun } from '@/lib/run';
import { Card, Chip, Field, Page, Row, Section, Txt, resultTone } from '@/lib/ui';

export default function Scenario() {
  const { id, runId, executionId } = useLocalSearchParams<{ id: string; runId: string; executionId: string }>();
  const { run, scenarios, page } = useRun(id, runId);
  const scenario = scenarios.find((row) => row.executionId === executionId);
  const timeline = run?.timelines.find((row) => row.executionId === executionId);
  const result = scenario ? feedResult(scenario) : null;

  return (
    <Page {...page} error={page.error ?? (run && !scenario ? 'This scenario is not part of the run.' : null)}>
      {scenario && result ? (
        <>
          <Card>
            <Chip label={feedResultStyles[result][0]} tone={resultTone(result)} />
            <Txt variant="title">{faultLabels[scenario.scenarioType]}</Txt>
            <Txt variant="mono" tone="dim">{`${scenario.httpMethod} ${scenario.path}`}</Txt>
            {scenario.failureReason ? <Txt variant="small">{scenario.failureReason}</Txt> : null}
            <Field label="Started" value={scenario.startedAt ? dateTime(scenario.startedAt) : PLACEHOLDER} />
            <Field label="Duration" value={duration(scenario.startedAt, scenario.completedAt)} />
          </Card>

          <Section title={`Findings (${scenario.findings.length})`}>
            {scenario.findings.length === 0 ? <Txt tone="dim">This scenario produced no finding.</Txt> : null}
            {scenario.findings.map((finding, index) => (
              <Card key={index}>
                <Chip label={outcomeLabels[finding.outcome]} tone={resultTone(finding.outcome)} />
                <Txt variant="small">{finding.summary}</Txt>
              </Card>
            ))}
          </Section>

          <Section title="Failure timeline">
            {timeline ? null : <Txt tone="dim">Request timelines are kept for the failing and degraded scenarios of a run.</Txt>}
            {timeline?.events.map((event, index) => (
              <Card key={index}>
                <Row>
                  <Txt variant="label" tone="dim">{`Attempt ${event.attemptNo}`}</Txt>
                  <Txt variant="small" tone="dim">
                    {new Date(event.startedAt).toLocaleTimeString()}
                  </Txt>
                </Row>
                <Txt variant="mono">{`${event.requestMethod} ${event.requestPath}`}</Txt>
                <Field label="Result" value={event.errorType ? `${event.outcomeType} · ${event.errorType}` : event.outcomeType} />
                <Field label="Status the client saw" value={event.clientStatusCode ?? PLACEHOLDER} />
                <Field label="Status from the target" value={event.upstreamStatusCode ?? PLACEHOLDER} />
                <Field label="Took" value={event.durationMs == null ? PLACEHOLDER : `${event.durationMs} ms`} />
              </Card>
            ))}
          </Section>
        </>
      ) : null}
    </Page>
  );
}
