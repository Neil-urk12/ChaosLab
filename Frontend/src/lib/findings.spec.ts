import { describe, expect, it } from 'vitest';
import type { RunFinding } from './api';
import { categoryScores, endpointScores, findingCategory, findingDetail, findingsCsv, weightedScore } from './findings';

const finding: RunFinding = {
	id: '1', severity: 'Critical', outcome: 'Failed', pattern: 'Idempotency', httpMethod: 'POST', path: '/payments',
	scenarioType: 'DuplicateRequest', summary: 'Duplicate charge, "twice"', evidence: {},
	recommendation: { title: 'Keys', text: '=HYPERLINK("x")' }
};

describe('findings', () => {
	it('derives category and detail from the scenario, falling back to pattern', () => {
		expect(findingCategory(finding)).toBe('Idempotency');
		expect(findingCategory({ ...finding, scenarioType: null, pattern: 'Retry' })).toBe('Retry');
		expect(findingDetail(finding)).toBe('Duplicate request');
		expect(findingDetail({ ...finding, evidence: { detail: 'Latency +3000 ms' } })).toBe('Latency +3000 ms');
	});

	it('quotes CSV cells and defuses formulas', () => {
		const [, row] = findingsCsv([finding]).split('\r\n');
		expect(row).toBe('Critical,Fail,POST,/payments,Idempotency,"Duplicate charge, ""twice""","\'=HYPERLINK(""x"")"');
		const [, tabbed] = findingsCsv([{ ...finding, summary: '\t=1+1', recommendation: null }]).split('\r\n');
		expect(tabbed).toBe('Critical,Fail,POST,/payments,Idempotency,\'\t=1+1,');
	});

	it('scores categories by mean outcome points and redistributes empty weights', () => {
		const findings: RunFinding[] = [
			finding,
			{ ...finding, id: '2', outcome: 'Passed' },
			{ ...finding, id: '3', outcome: 'Inconclusive' },
			{ ...finding, id: '4', scenarioType: 'Latency', outcome: 'Degraded', path: '/health', httpMethod: 'GET' },
			{ ...finding, id: '5', scenarioType: 'Baseline', outcome: 'Failed' }
		];
		const categories = categoryScores(findings);
		const idempotency = categories.find((c) => c.label === 'Idempotency')!;
		expect([idempotency.score, idempotency.findings, idempotency.inconclusive]).toEqual([50, 3, 1]);
		expect(categories.find((c) => c.label === 'Timeout handling')!.score).toBe(50);
		expect(categories.find((c) => c.label === 'Retry behavior')!.score).toBeNull();
		// Timeout 50 × 20 and Idempotency 50 × 10 over the 30 points of weight that were scored.
		expect(weightedScore(categories)).toBe(50);
		expect(weightedScore(categoryScores([{ ...finding, outcome: 'NotApplicable' }]))).toBeNull();
		expect(endpointScores(findings)).toEqual([
			{ httpMethod: 'POST', path: '/payments', score: 50 },
			{ httpMethod: 'GET', path: '/health', score: 50 }
		]);
	});
});
