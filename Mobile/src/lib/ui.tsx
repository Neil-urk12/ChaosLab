import type { ReactNode } from 'react';
import { ActivityIndicator, Pressable, RefreshControl, ScrollView, StyleSheet, Text, View, type TextProps } from 'react-native';

import type { FindingOutcome, Severity, TestRunStatus } from '@web/api';
import type { FeedResult } from '@web/runs';

import { useTheme, type Theme } from '@/constants/theme';

export type Tone = 'good' | 'warn' | 'bad' | 'accent' | 'dim';

export const resultTone = (result: FeedResult | FindingOutcome): Tone =>
  result === 'Passed' ? 'good' : result === 'Degraded' ? 'warn' : result === 'Failed' ? 'bad' : result === 'Running' ? 'accent' : 'dim';

export const statusTone: Record<TestRunStatus, Tone> = {
  Queued: 'accent',
  Running: 'accent',
  Completed: 'good',
  PartiallyCompleted: 'warn',
  Cancelled: 'dim',
  Failed: 'bad',
};

export const severityTone = (severity: Severity): Tone =>
  severity === 'Critical' || severity === 'High' ? 'bad' : severity === 'Medium' ? 'warn' : 'dim';

export const scoreTone = (score: number | null | undefined): Tone => (score == null ? 'dim' : score >= 80 ? 'good' : score >= 60 ? 'warn' : 'bad');

type TxtProps = TextProps & { variant?: 'body' | 'small' | 'label' | 'title' | 'big' | 'mono'; tone?: Tone | 'text' };

export function Txt({ variant = 'body', tone = 'text', style, ...rest }: TxtProps) {
  const theme = useTheme();
  return <Text style={[styles[variant], { color: theme[tone] }, style]} {...rest} />;
}

export function Chip({ label, tone }: { label: string; tone: Tone }) {
  const theme = useTheme();
  return (
    <View style={[styles.chip, { borderColor: theme[tone] }]}>
      <Text style={[styles.chipText, { color: theme[tone] }]}>{label}</Text>
    </View>
  );
}

// A bordered block; tappable when it has onPress.
export function Card({ children, onPress, label }: { children: ReactNode; onPress?: () => void; label?: string }) {
  const theme = useTheme();
  const style = [styles.card, { backgroundColor: theme.card, borderColor: theme.border }];
  if (!onPress) return <View style={style}>{children}</View>;
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      onPress={onPress}
      style={({ pressed }) => [style, pressed && { opacity: 0.6 }]}>
      {children}
    </Pressable>
  );
}

export function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <View style={styles.section}>
      <Txt variant="label" tone="dim" accessibilityRole="header">
        {title}
      </Txt>
      {children}
    </View>
  );
}

// A label with its value on the right; long values wrap under the label's line.
export function Field({ label, value }: { label: string; value: ReactNode }) {
  return (
    <View style={styles.field}>
      <Txt variant="small" tone="dim">
        {label}
      </Txt>
      {typeof value === 'string' || typeof value === 'number' ? (
        <Txt variant="small" style={styles.fieldValue}>
          {value}
        </Txt>
      ) : (
        value
      )}
    </View>
  );
}

export function Row({ children }: { children: ReactNode }) {
  return <View style={styles.row}>{children}</View>;
}

export function Button({ label, onPress, disabled }: { label: string; onPress: () => void; disabled?: boolean }) {
  const theme = useTheme();
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [styles.button, { backgroundColor: theme.accent }, (pressed || disabled) && { opacity: 0.6 }]}>
      <Text style={[styles.buttonText, { color: theme.background }]}>{label}</Text>
    </Pressable>
  );
}

// A scrolling screen body with pull-to-refresh. Shows a spinner until the first data arrives, and the error
// instead of the content when there is nothing to show.
export function Page(props: { loading: boolean; error: string | null; refreshing: boolean; onRefresh: () => void; children: ReactNode }) {
  const theme = useTheme();
  return (
    <ScrollView
      contentInsetAdjustmentBehavior="automatic"
      contentContainerStyle={styles.page}
      style={{ backgroundColor: theme.background }}
      refreshControl={<RefreshControl refreshing={props.refreshing} onRefresh={props.onRefresh} tintColor={theme.dim} />}>
      {props.error ? (
        <Txt tone={props.loading ? 'dim' : 'bad'} accessibilityRole="alert">
          {props.error}
        </Txt>
      ) : null}
      {props.loading ? props.error ? null : <ActivityIndicator color={theme.dim} style={styles.spinner} /> : props.children}
    </ScrollView>
  );
}

// A full-screen message, used before the navigator exists.
export function Notice({ title, text, action }: { title?: string; text?: string; action?: [string, () => void] }) {
  const theme = useTheme();
  return (
    <View style={[styles.notice, { backgroundColor: theme.background }]}>
      {title ? <Txt variant="title">{title}</Txt> : <ActivityIndicator color={theme.dim} />}
      {text ? <Txt tone="dim">{text}</Txt> : null}
      {action ? <Button label={action[0]} onPress={action[1]} /> : null}
    </View>
  );
}

export const inputStyle = (theme: Theme) => [styles.input, { color: theme.text, borderColor: theme.border, backgroundColor: theme.card }];

const styles = StyleSheet.create({
  body: { fontSize: 16, lineHeight: 23 },
  small: { fontSize: 14, lineHeight: 20 },
  label: { fontSize: 12, lineHeight: 16, fontWeight: '600', letterSpacing: 0.8, textTransform: 'uppercase' },
  title: { fontSize: 20, lineHeight: 26, fontWeight: '600' },
  big: { fontSize: 44, lineHeight: 50, fontWeight: '700', fontVariant: ['tabular-nums'] },
  mono: { fontSize: 13, lineHeight: 19, fontFamily: 'monospace' },
  chip: { borderWidth: 1, borderRadius: 999, paddingHorizontal: 8, paddingVertical: 2, alignSelf: 'flex-start' },
  chipText: { fontSize: 12, lineHeight: 16, fontWeight: '600' },
  card: { borderWidth: 1, borderRadius: 10, padding: 14, gap: 6 },
  section: { gap: 8 },
  field: { flexDirection: 'row', justifyContent: 'space-between', gap: 16 },
  fieldValue: { flexShrink: 1, textAlign: 'right' },
  row: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: 8 },
  // 44pt minimum touch target.
  button: { minHeight: 44, borderRadius: 10, paddingHorizontal: 16, alignItems: 'center', justifyContent: 'center' },
  buttonText: { fontSize: 16, fontWeight: '600' },
  page: { padding: 16, gap: 20 },
  spinner: { marginTop: 48 },
  notice: { flex: 1, justifyContent: 'center', padding: 24, gap: 12 },
  input: { minHeight: 44, borderWidth: 1, borderRadius: 10, paddingHorizontal: 12, fontSize: 16 },
});
