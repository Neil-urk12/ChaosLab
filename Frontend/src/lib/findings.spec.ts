import { describe, expect, it } from 'vitest';
import type { RunFinding } from './api';
import { findingCategory, findingDetail, findingsCsv } from './findings';

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
});
