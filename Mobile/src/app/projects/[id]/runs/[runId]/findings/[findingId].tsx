import { useLocalSearchParams } from 'expo-router';

import { findingCategory, outcomeLabels } from '@web/findings';
import { faultLabels } from '@web/scenarios';

import { useRun } from '@/lib/run';
import { Card, Chip, Field, Page, Row, Section, Txt, resultTone, severityTone } from '@/lib/ui';

export default function Finding() {
  const { id, runId, findingId } = useLocalSearchParams<{ id: string; runId: string; findingId: string }>();
  const { findings, page } = useRun(id, runId);
  const finding = findings?.find((row) => row.id === findingId);
  const evidence = finding?.evidence;
  const interpretation = evidence?.interpretation;

  return (
    <Page {...page} error={page.error ?? (findings && !finding ? 'This finding is not part of the run.' : null)}>
      {finding ? (
        <>
          <Card>
            <Row>
              <Chip label={outcomeLabels[finding.outcome]} tone={resultTone(finding.outcome)} />
              <Chip label={finding.severity} tone={severityTone(finding.severity)} />
            </Row>
            <Txt>{finding.summary}</Txt>
            <Txt variant="mono" tone="dim">{`${finding.httpMethod} ${finding.path}`}</Txt>
            <Field label="Category" value={findingCategory(finding)} />
            {finding.scenarioType ? <Field label="Fault" value={faultLabels[finding.scenarioType]} /> : null}
          </Card>

          <Section title="Evidence">
            <Card>
              {evidence?.detail ? <Txt variant="small">{evidence.detail}</Txt> : null}
              {evidence?.expected ? <Field label="Expected" value={evidence.expected} /> : null}
              {evidence?.observed ? <Field label="Observed" value={evidence.observed} /> : null}
              {evidence?.invariantViolated ? <Txt variant="small" tone="bad">A developer invariant was violated.</Txt> : null}
              {evidence?.facts?.map((fact) => <Field key={fact.label} label={fact.label} value={fact.value} />)}
              {evidence?.detail || evidence?.expected || evidence?.observed || evidence?.invariantViolated || evidence?.facts?.length ? null : (
                <Txt tone="dim">No evidence was recorded for this finding.</Txt>
              )}
            </Card>
          </Section>

          {interpretation?.observed || interpretation?.suggests || interpretation?.notProven ? (
            <Section title="What it means">
              <Card>
                {interpretation.observed ? <Field label="Observed" value={interpretation.observed} /> : null}
                {interpretation.suggests ? <Field label="Suggests" value={interpretation.suggests} /> : null}
                {interpretation.notProven ? <Field label="Not proven" value={interpretation.notProven} /> : null}
              </Card>
            </Section>
          ) : null}

          <Section title="Recommendation">
            {finding.recommendation ? (
              <Card>
                <Txt variant="title">{finding.recommendation.title}</Txt>
                <Txt variant="small">{finding.recommendation.text}</Txt>
              </Card>
            ) : (
              <Txt tone="dim">No recommendation for this finding.</Txt>
            )}
          </Section>
        </>
      ) : null}
    </Page>
  );
}
