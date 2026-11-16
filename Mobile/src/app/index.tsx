import { Stack, useRouter } from 'expo-router';
import { Pressable } from 'react-native';

import type { ProjectListRow } from '@web/api';
import { relative, score } from '@web/format';

import { useLoad } from '@/lib/api';
import { useSession } from '@/lib/session';
import { Card, Chip, Page, Row, Txt, scoreTone } from '@/lib/ui';

export default function Projects() {
  const router = useRouter();
  const { signOut } = useSession();
  const { data, error, refreshing, refresh } = useLoad<ProjectListRow[]>('/api/projects');

  return (
    <Page loading={!data} error={error} refreshing={refreshing} onRefresh={refresh}>
      <Stack.Screen
        options={{
          headerRight: () => (
            <Pressable accessibilityRole="button" hitSlop={12} onPress={signOut}>
              <Txt tone="accent">Sign out</Txt>
            </Pressable>
          ),
        }}
      />
      {data?.length === 0 ? <Txt tone="dim">No projects yet. Projects you are a member of appear here.</Txt> : null}
      {data?.map((project) => (
        <Card key={project.id} label={project.name} onPress={() => router.push(`/projects/${project.id}`)}>
          <Row>
            <Txt variant="title" style={{ flex: 1 }}>
              {project.name}
            </Txt>
            <Chip label={project.latestScore == null ? 'No score' : `Score ${score(project.latestScore)}`} tone={scoreTone(project.latestScore)} />
          </Row>
          {project.description ? (
            <Txt variant="small" tone="dim" numberOfLines={2}>
              {project.description}
            </Txt>
          ) : null}
          {project.baseUrl ? <Txt variant="mono">{project.baseUrl}</Txt> : null}
          <Txt variant="small" tone="dim">
            {project.lastRunAt ? `Last run ${relative(project.lastRunAt)}` : 'No runs yet'}
          </Txt>
        </Card>
      ))}
    </Page>
  );
}
