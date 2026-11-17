import type { FindingOutcome, RunFinding, ScoringCategory, Severity } from './api';
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

const categoryLabels: Record<ScoringCategory, string> = {
	Timeout: 'Timeout',
	Retry: 'Retry',
	GracefulDegradation: 'Graceful degradation',
	RateLimit: 'Rate-limit',
	Idempotency: 'Idempotency',
	AuthenticationFailure: 'Auth failure',
	ResponseValidation: 'Response validation',
	DependencyFailure: 'Dependency failure'
};

// The finding's own scoring category: one scenario can yield findings in several. Without one, the scenario's fault,
// then the pattern.
export const findingCategory = (f: Pick<RunFinding, 'scenarioType' | 'pattern'> & { category?: ScoringCategory | null }) =>
	f.category ? categoryLabels[f.category] : f.scenarioType ? categories[f.scenarioType] : f.pattern;

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

// Scoring categories in the backend's order (Scorer.cs), by their full names. Weights and scores are stored by the backend.
export const scoringCategoryLabels: Record<ScoringCategory, string> = {
	Timeout: 'Timeout handling',
	Retry: 'Retry behavior',
	GracefulDegradation: 'Graceful degradation',
	RateLimit: 'Rate-limit handling',
	Idempotency: 'Idempotency',
	AuthenticationFailure: 'Authentication failure handling',
	ResponseValidation: 'Response validation',
	DependencyFailure: 'Dependency-failure handling'
};

