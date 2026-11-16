import { Stack, useLocalSearchParams, useRouter } from 'expo-router';

import type { FindingOutcome } from '@web/api';
import { isScored, outcomeLabels } from '@web/findings';
import { PLACEHOLDER, dateTime, duration, score, scoreLabel } from '@web/format';
import { feedResult, feedResultStyles, runLabel, statusStyles } from '@web/runs';
import { faultLabels } from '@web/scenarios';

import { useRun, worstFirst } from '@/lib/run';
import { Card, Chip, Field, Page, Row, Section, Txt, resultTone, scoreTone, severityTone, statusTone } from '@/lib/ui';

const OUTCOMES: FindingOutcome[] = ['Passed', 'Degraded', 'Failed', 'Inconclusive', 'NotApplicable'];

export default function Run() {
  const router = useRouter();
  const { id, runId } = useLocalSearchParams<{ id: string; runId: string }>();
  const { run, findings = [], scenarios, page } = useRun(id, runId);
  const here = `/projects/${id}/runs/${runId}`;

  const count = (outcome: FindingOutcome) => findings.filter((finding) => finding.outcome === outcome).length;
  const scored = findings.filter(isScored).length;
  // Same definition as the web app: scored findings over scored plus Inconclusive.
  const coverage = scored + count('Inconclusive') ? Math.round((100 * scored) / (scored + count('Inconclusive'))) : null;
  const change = run?.score != null && run.previous ? Math.round(run.score - run.previous.score) : null;
  const active = run?.status === 'Queued' || run?.status === 'Running';
  const recommended = findings.filter((finding) => finding.recommendation);

  return (
    <Page {...page}>
      {run ? (
        <>
          <Stack.Screen options={{ title: runLabel(run) }} />

          <Card>
            <Row>
              <Chip label={statusStyles[run.status][0]} tone={statusTone[run.status]} />
              {active ? <Txt variant="small" tone="dim">Pull down to refresh</Txt> : null}
            </Row>
            {run.failureMessage ? <Txt variant="small" tone="bad">{run.failureMessage}</Txt> : null}
            <Row>
              <Txt variant="big" tone={scoreTone(run.score)}>
                {score(run.score)}
              </Txt>
              {run.score == null ? (
                <Txt variant="small" tone="dim" style={{ flex: 1 }}>
                  {active ? 'Scored when the run finishes' : 'Not scored: a run is scored only when every scenario ran'}
                </Txt>
              ) : (
                <>
                  <Chip label={scoreLabel(run.score)} tone={scoreTone(run.score)} />
                  {change == null ? null : <Txt variant="small" tone="dim">{`${change > 0 ? '+' : ''}${change} from ${runLabel(run.previous!)}`}</Txt>}
                </>
              )}
            </Row>
            <Field label="Evidence coverage" value={coverage == null ? PLACEHOLDER : `${coverage}%`} />
            <Field label="Scenarios" value={`${run.completedScenarios}/${run.totalScenarios}${run.skippedScenarios ? ` · ${run.skippedScenarios} skipped` : ''}`} />
            <Field label="Environment" value={run.environmentName} />
            <Field label="Started by" value={run.requestedBy} />
            <Field label="Date" value={dateTime(run.startedAt ?? run.createdAt)} />
            <Field label="Duration" value={duration(run.startedAt, run.completedAt)} />
            <Field label="Scoring version" value={run.scoringVersion} />
          </Card>

          <Section title={`Findings (${findings.length})`}>
            {findings.length === 0 ? <Txt tone="dim">{active ? 'No findings yet.' : 'This run produced no findings.'}</Txt> : null}
            <Row>
              {OUTCOMES.filter(count).map((outcome) => (
                <Chip key={outcome} label={`${outcomeLabels[outcome]} ${count(outcome)}`} tone={resultTone(outcome)} />
              ))}
            </Row>
            {findings.toSorted(worstFirst).map((finding) => (
              <Card key={finding.id} label={finding.summary} onPress={() => router.push(`${here}/findings/${finding.id}`)}>
                <Row>
                  <Chip label={outcomeLabels[finding.outcome]} tone={resultTone(finding.outcome)} />
                  <Chip label={finding.severity} tone={severityTone(finding.severity)} />
                </Row>
                <Txt variant="small">{finding.summary}</Txt>
                <Txt variant="mono" tone="dim">{`${finding.httpMethod} ${finding.path}`}</Txt>
              </Card>
            ))}
          </Section>

          <Section title={`Recommendations (${recommended.length})`}>
            {recommended.length === 0 ? <Txt tone="dim">No recommendations for this run.</Txt> : null}
            {recommended.map((finding) => (
              <Card key={finding.id} label={finding.recommendation!.title} onPress={() => router.push(`${here}/findings/${finding.id}`)}>
                <Txt variant="small">{finding.recommendation!.title}</Txt>
                <Txt variant="mono" tone="dim">{`${finding.httpMethod} ${finding.path}`}</Txt>
              </Card>
            ))}
          </Section>

          {run.timelines.length ? (
            <Section title={`Failure timelines (${run.timelines.length})`}>
              {run.timelines.map((timeline) => (
                <Card
                  key={timeline.executionId}
                  label={`Timeline of ${faultLabels[timeline.scenarioType]} on ${timeline.httpMethod} ${timeline.path}`}
                  onPress={() => router.push(`${here}/scenarios/${timeline.executionId}`)}>
                  <Txt variant="small">{faultLabels[timeline.scenarioType]}</Txt>
                  <Txt variant="mono" tone="dim">{`${timeline.httpMethod} ${timeline.path}`}</Txt>
                  <Txt variant="small" tone="dim">{`${timeline.events.length} requests`}</Txt>
                </Card>
              ))}
            </Section>
          ) : null}

          {/* shortcut: every scenario renders at once (a run has at most a few hundred); switch to a FlatList if runs grow past that. */}
          <Section title={`Scenario results (${scenarios.length})`}>
            {scenarios.length === 0 ? <Txt tone="dim">No scenario has started yet.</Txt> : null}
            {scenarios.map((scenario) => {
              const result = feedResult(scenario);
              return (
                <Card
                  key={scenario.executionId}
                  label={`${faultLabels[scenario.scenarioType]} on ${scenario.httpMethod} ${scenario.path}`}
                  onPress={() => router.push(`${here}/scenarios/${scenario.executionId}`)}>
                  <Row>
                    <Chip label={feedResultStyles[result][0]} tone={resultTone(result)} />
                    <Txt variant="small" style={{ flex: 1 }}>
                      {faultLabels[scenario.scenarioType]}
                    </Txt>
                  </Row>
                  <Txt variant="mono" tone="dim">{`${scenario.httpMethod} ${scenario.path}`}</Txt>
                </Card>
              );
            })}
          </Section>
        </>
      ) : null}
    </Page>
  );
}
