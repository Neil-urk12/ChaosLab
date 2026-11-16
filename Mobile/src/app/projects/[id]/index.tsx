import { Stack, useLocalSearchParams, useRouter } from 'expo-router';

import type { ProjectDetail } from '@web/api';
import { SCORING_CATEGORIES } from '@web/findings';
import { PLACEHOLDER, dateTime, relative, score, scoreLabel } from '@web/format';
import { statusStyles } from '@web/runs';

import { useLoad } from '@/lib/api';
import { Card, Chip, Field, Page, Row, Section, Txt, scoreTone, statusTone } from '@/lib/ui';

// The backend stores category scores under its own names; show the same labels as the web app.
const categoryLabel = (name: string) => SCORING_CATEGORIES.find(([, , stored]) => stored === name)?.[0] ?? name;

export default function Project() {
  const router = useRouter();
  const { id } = useLocalSearchParams<{ id: string }>();
  const { data: project, error, refreshing, refresh } = useLoad<ProjectDetail>(`/api/projects/${id}`);
  const latest = project?.latest;
  const change = latest?.previousScore == null ? null : Math.round(latest.score - latest.previousScore);

  return (
    <Page loading={!project} error={error} refreshing={refreshing} onRefresh={refresh}>
      {project ? (
        <>
          <Stack.Screen options={{ title: project.name }} />
          {project.description ? <Txt tone="dim">{project.description}</Txt> : null}

          <Section title="Latest Resilience Score">
            <Card>
              {latest ? (
                <>
                  <Row>
                    <Txt variant="big" tone={scoreTone(latest.score)}>
                      {score(latest.score)}
                    </Txt>
                    <Chip label={scoreLabel(latest.score)} tone={scoreTone(latest.score)} />
                    {change == null ? null : <Txt variant="small" tone="dim">{`${change > 0 ? '+' : ''}${change} from the previous run`}</Txt>}
                  </Row>
                  <Txt variant="small" tone="dim">{`Scored ${relative(latest.completedAt)}`}</Txt>
                  {latest.breakdown?.map((category) => (
                    <Field key={category.category} label={categoryLabel(category.category)} value={score(category.score)} />
                  ))}
                </>
              ) : (
                <Txt tone="dim">No scored run yet. A run is scored when every scenario in it ran.</Txt>
              )}
            </Card>
          </Section>

          <Section title="API">
            <Card>
              <Field label="Environment" value={project.defaultEnvironment?.name ?? PLACEHOLDER} />
              <Field label="Base URL" value={project.defaultEnvironment?.baseUrl ?? PLACEHOLDER} />
              <Field label="Specification" value={project.spec ? (project.spec.title ?? 'Untitled') : 'Not imported'} />
              {project.spec ? (
                <>
                  <Field label="OpenAPI version" value={project.spec.openApiVersion} />
                  <Field label="Imported" value={dateTime(project.spec.importedAt)} />
                </>
              ) : null}
              <Field label="Endpoints" value={project.endpointCount} />
              <Field label="Scenarios" value={project.scenarioCount} />
            </Card>
          </Section>

          <Section title="Recent runs">
            {project.recentRuns.length === 0 ? <Txt tone="dim">No runs yet. Runs are started from the web app.</Txt> : null}
            {project.recentRuns.map((run) => (
              <Card key={run.id} label={`Run from ${dateTime(run.createdAt)}`} onPress={() => router.push(`/projects/${id}/runs/${run.id}`)}>
                <Row>
                  <Chip label={statusStyles[run.status][0]} tone={statusTone[run.status]} />
                  {run.score == null ? null : <Chip label={`Score ${score(run.score)}`} tone={scoreTone(run.score)} />}
                </Row>
                <Txt variant="small">{dateTime(run.createdAt)}</Txt>
                <Txt variant="small" tone="dim">
                  {`${run.completedScenarios}/${run.totalScenarios} scenarios · ${run.failedFindings} failed · ${run.environmentName}`}
                </Txt>
              </Card>
            ))}
          </Section>
        </>
      ) : null}
    </Page>
  );
}
