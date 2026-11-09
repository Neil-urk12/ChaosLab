import type { FindingOutcome, RunFinding, Severity } from './api';
import { categories, faultLabels } from './scenarios';

export const outcomeLabels: Record<FindingOutcome, string> = {
	Passed: 'Pass',
	Degraded: 'Degraded',
	Failed: 'Fail',
	Inconclusive: 'Inconclusive',
	NotApplicable: 'Not applicable'
};

// Highest first: table filter order and the severity sort.
export const SEVERITIES: Severity[] = ['Critical', 'High', 'Medium', 'Low', 'Info'];

// Scoring category from the scenario's fault; a finding without an execution falls back to its pattern.
export const findingCategory = (f: Pick<RunFinding, 'scenarioType' | 'pattern'>) =>
	f.scenarioType ? categories[f.scenarioType] : f.pattern;

export const findingDetail = (f: Pick<RunFinding, 'scenarioType' | 'evidence'>) =>
	(typeof f.evidence?.detail === 'string' && f.evidence.detail) || (f.scenarioType ? faultLabels[f.scenarioType] : '');

// RFC 4180 quoting; a leading =, +, -, @, tab or CR (OWASP CSV injection list) is prefixed with ' so spreadsheets don't run it as a formula.
const cell = (value: string) => {
	const safe = /^[=+\-@\t\r]/.test(value) ? `'${value}` : value;
	return /[",\r\n]/.test(safe) ? `"${safe.replaceAll('"', '""')}"` : safe;
};

export function findingsCsv(findings: RunFinding[]): string {
	const rows = [
		['Severity', 'Outcome', 'Method', 'Endpoint', 'Category', 'Summary', 'Recommendation'],
		...findings.map((f) => [
			f.severity,
			outcomeLabels[f.outcome],
			f.httpMethod,
			f.path,
			findingCategory(f),
			f.summary,
			f.recommendation?.text ?? ''
		])
	];
	return rows.map((row) => row.map(cell).join(',')).join('\r\n');
}
