import { describe, expect, it } from 'vitest';
import type { FindingOutcome, RunFinding, ScenarioType } from './api';
import { compareFindings, pairScores, scoreChange } from './compare';

const finding = (path: string, scenarioType: ScenarioType, outcome: FindingOutcome, pattern: RunFinding['pattern'] = 'Retry'): RunFinding => ({
	id: `${path}-${scenarioType}-${outcome}`, severity: 'High', outcome, pattern, category: null, httpMethod: 'GET', path,
	executionId: null, scenarioId: null, scenarioType, summary: '', evidence: {}, recommendation: null
});
const keys = (changes: { key: string }[]) => changes.map((c) => c.key);

describe('compare', () => {
	it('sorts checks by how their outcome moved', () => {
		const before = [
			finding('/a', 'ServerErrorStorm', 'Passed'),
			finding('/b', 'ServerErrorStorm', 'Failed'),
			finding('/c', 'ServerErrorStorm', 'Degraded'),
			finding('/d', 'ServerErrorStorm', 'Failed'),
			finding('/e', 'ServerErrorStorm', 'Failed'),
			finding('/f', 'ServerErrorStorm', 'Passed'),
			finding('/g', 'ServerErrorStorm', 'Inconclusive')
		];
		const after = [
			finding('/a', 'ServerErrorStorm', 'Degraded'),
			finding('/b', 'ServerErrorStorm', 'Passed'),
			finding('/c', 'ServerErrorStorm', 'Degraded'),
			finding('/d', 'ServerErrorStorm', 'Inconclusive'),
			// /e was not run again.
			finding('/f', 'ServerErrorStorm', 'Passed'),
			finding('/g', 'ServerErrorStorm', 'Failed'),
			finding('/h', 'ServerErrorStorm', 'Failed')
		];
		const changes = compareFindings(before, after);
		expect(keys(changes.worse)).toEqual(['GET /a|ServerErrorStorm|Retry', 'GET /g|ServerErrorStorm|Retry', 'GET /h|ServerErrorStorm|Retry']);
		expect(keys(changes.better)).toEqual(['GET /b|ServerErrorStorm|Retry']);
		expect(keys(changes.stillFailing)).toEqual(['GET /c|ServerErrorStorm|Retry']);
		expect(keys(changes.unjudged)).toEqual(['GET /d|ServerErrorStorm|Retry', 'GET /e|ServerErrorStorm|Retry']);
		expect(changes.unjudged[1]).toMatchObject({ after: null, before: { outcome: 'Failed' } });
	});

	it('matches by fault and pattern, and keeps the worst judged outcome of a check judged twice', () => {
		const changes = compareFindings(
			[finding('/a', 'Latency', 'Failed'), finding('/a', 'ServerErrorStorm', 'Passed', 'Timeout')],
			[finding('/a', 'Latency', 'Passed'), finding('/a', 'Latency', 'Inconclusive'), finding('/a', 'ServerErrorStorm', 'Passed'), finding('/a', 'ServerErrorStorm', 'Failed', 'Timeout')]
		);
		expect(keys(changes.better)).toEqual(['GET /a|Latency|Retry']);
		expect(keys(changes.worse)).toEqual(['GET /a|ServerErrorStorm|Timeout']);
		expect(changes.stillFailing).toEqual([]);
	});

	it('pairs scores by key in the newer run order', () => {
		const rows = pairScores([{ k: 'x', s: 1 }, { k: 'old', s: 2 }], [{ k: 'new', s: 3 }, { k: 'x', s: 4 }], (r) => r.k);
		expect(rows.map((r) => [r.key, r.before?.s ?? null, r.after?.s ?? null])).toEqual([['new', null, 3], ['x', 1, 4], ['old', 2, null]]);
	});

	it('rounds the change and needs both scores', () => {
		expect(scoreChange(70.4, 82.9)).toBe(13);
		expect(scoreChange(80, 75)).toBe(-5);
		expect(scoreChange(null, 75)).toBeNull();
		expect(scoreChange(80, undefined)).toBeNull();
	});
});
