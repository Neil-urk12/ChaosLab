import { describe, expect, it } from 'vitest';
import type { TestRunStatus } from './api';
import { canStartRuns, clockDuration, isGuid, phaseLabel, rerunScenarioIds, confirmationRows, needsConfirmation, compareProblem, dayRange, DEFAULT_DAYS, feedResult, pageWindow, remainingMs, runLabel } from './runs';

const run = (environmentName = 'Staging', scoringVersion = '1.0', status: TestRunStatus = 'Completed') => ({ environmentName, scoringVersion, status });

describe('runs', () => {
	it('pads run numbers', () => {
		expect(runLabel({ number: 27 })).toBe('RUN-027');
		expect(runLabel({ number: 1234 })).toBe('RUN-1234');
	});

	it('keeps only date ranges the menu offers', () => {
		expect(dayRange('7')).toBe('7');
		expect(dayRange('all')).toBe('all');
		expect(dayRange('14')).toBe(DEFAULT_DAYS);
		expect(dayRange(null)).toBe(DEFAULT_DAYS);
	});

	it('shows first, last and nearby pages with gaps', () => {
		expect(pageWindow(1, 1)).toEqual([1]);
		expect(pageWindow(2, 5)).toEqual([1, 2, 3, 4, 5]);
		expect(pageWindow(1, 100)).toEqual([1, 2, 3, null, 100]);
		expect(pageWindow(50, 100)).toEqual([1, null, 48, 49, 50, 51, 52, null, 100]);
		expect(pageWindow(100, 100)).toEqual([1, null, 98, 99, 100]);
	});

	it('only compares two runs on the same environment and scoring version', () => {
		expect(compareProblem([run(), run()])).toBeNull();
		expect(compareProblem([run()])).toMatch(/exactly two/);
		expect(compareProblem([run(), run('Prod-like test')])).toMatch(/environments/);
		expect(compareProblem([run(), run('Staging', '2.0')])).toMatch(/scoring versions/);
		expect(compareProblem([run(), run('Staging', '1.0', 'Running')])).toMatch(/not finished/);
		expect(compareProblem([run('Staging', '1.0', 'Queued'), run()])).toMatch(/not finished/);
		expect(compareProblem([run(), run('Staging', '1.0', 'Cancelled')])).toBeNull();
	});

	it('ranks a feed row by its worst finding, then the execution outcome', () => {
		const row = (status: 'Running' | 'Completed' | 'Failed' | 'Skipped', outcome: 'Passed' | 'Failed' | null = null, findings: ('Degraded' | 'Passed')[] = []) =>
			({ status, outcome, findings: findings.map((o) => ({ outcome: o, summary: '' })) });
		expect(feedResult(row('Running'))).toBe('Running');
		expect(feedResult(row('Skipped'))).toBe('Skipped');
		expect(feedResult(row('Failed', 'Failed'))).toBe('RunnerError');
		expect(feedResult(row('Completed', 'Passed', ['Degraded', 'Passed']))).toBe('Degraded');
		expect(feedResult(row('Completed', 'Failed'))).toBe('Failed');
		expect(feedResult(row('Completed'))).toBe('Evaluating');
	});

	it('estimates the remaining time from the pace so far', () => {
		expect(remainingMs(60_000, 0, 10)).toBeNull();
		expect(remainingMs(60_000, 2, 10)).toBe(240_000);
		expect(remainingMs(60_000, 10, 10)).toBe(0);
		expect(clockDuration(185_000)).toBe('3m 05s');
		expect(clockDuration(42_000)).toBe('42s');
	});

	it('asks to confirm state-changing methods and duplicate requests', () => {
		expect(needsConfirmation({ httpMethod: 'GET', type: 'Latency' })).toBe(false);
		expect(needsConfirmation({ httpMethod: 'GET', type: 'DuplicateRequest' })).toBe(true);
		expect(needsConfirmation({ httpMethod: 'DELETE', type: 'Latency' })).toBe(true);
		const s = (endpointId: string, httpMethod: string, type: 'Latency' | 'DuplicateRequest') => ({ endpointId, httpMethod, path: `/${endpointId}`, type });
		expect(confirmationRows([s('a', 'POST', 'Latency'), s('b', 'GET', 'Latency'), s('a', 'POST', 'DuplicateRequest')])).toEqual([
			{ httpMethod: 'POST', path: '/a', count: 2, duplicates: 1 }
		]);
	});

	it('lets owners and editors start runs, not viewers or non-members', () => {
		const project = { members: [{ userId: 'o', role: 'Owner' as const }, { userId: 'e', role: 'Editor' as const }, { userId: 'v', role: 'Viewer' as const }] };
		expect(['o', 'e', 'v', 'x'].map((id) => canStartRuns(project, { id }))).toEqual([true, true, false, false]);
	});

	it('reruns each failed or degraded scenario once', () => {
		const f = (outcome: 'Failed' | 'Degraded' | 'Passed' | 'Inconclusive', scenarioId: string | null) => ({ outcome, scenarioId });
		expect(rerunScenarioIds([f('Failed', 'a'), f('Degraded', 'a'), f('Degraded', 'b'), f('Passed', 'c'), f('Inconclusive', 'd'), f('Failed', null)])).toEqual(['a', 'b']);
		expect(rerunScenarioIds([])).toEqual([]);
	});

	it('accepts only GUIDs as run links', () => {
		expect(isGuid('00000000-0000-4000-8000-000000000007')).toBe(true);
		expect(isGuid('a-30')).toBe(false);
		expect(isGuid(null)).toBe(false);
	});

	it('labels the fault start and recovery requests of a timeline', () => {
		const events = (['baseline', 'fault', 'fault', 'recovery', 'recovery', 'readBack', null] as const).map((phase) => ({ phase }));
		expect(events.map((_, i) => phaseLabel(events, i))).toEqual(['Baseline', 'Fault phase starts', 'During fault', 'Recovery', 'Recovery', null, null]);
	});
});
