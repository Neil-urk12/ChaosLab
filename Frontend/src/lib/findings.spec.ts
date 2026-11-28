import { describe, expect, it } from 'vitest';
import type { RunFinding } from './api';
import { findingCategory, findingDetail, findingsCsv } from './findings';

const finding: RunFinding = {
	id: '1', severity: 'Critical', outcome: 'Failed', pattern: 'Idempotency', category: null, httpMethod: 'POST', path: '/payments', executionId: 'e1',
	scenarioType: 'DuplicateRequest', summary: 'Duplicate charge, "twice"', evidence: {},
	recommendation: { title: 'Keys', text: '=HYPERLINK("x")' }
};

describe('findings', () => {
	it('derives category and detail from the scenario, falling back to pattern', () => {
		expect(findingCategory(finding)).toBe('Idempotency');
		expect(findingCategory({ ...finding, scenarioType: null, pattern: 'Retry' })).toBe('Retry');
		// A dependency failure's fallback finding is scored under its own category, not the scenario's.
		const fallback = { ...finding, scenarioType: 'DnsFailure', pattern: 'Fallback', category: 'GracefulDegradation', outcome: 'Passed' } as const;
		expect(findingCategory(fallback)).toBe('Graceful degradation');
		expect(findingDetail(finding)).toBe('Duplicate request');
		expect(findingDetail({ ...finding, evidence: { detail: 'Latency +3000 ms' } })).toBe('Latency +3000 ms');
	});

	it('quotes CSV cells and defuses formulas', () => {
		const [, row] = findingsCsv([finding]).split('\r\n');
		expect(row).toBe('Critical,Fail,POST,/payments,Idempotency,"Duplicate charge, ""twice""","\'=HYPERLINK(""x"")"');
		const [, tabbed] = findingsCsv([{ ...finding, summary: '\t=1+1', recommendation: null }]).split('\r\n');
		expect(tabbed).toBe('Critical,Fail,POST,/payments,Idempotency,\'\t=1+1,');
	});
});
