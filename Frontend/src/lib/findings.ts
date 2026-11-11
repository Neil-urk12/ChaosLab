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

// Scoring categories and their locked weights, scoring version 1.0 (PROJECT.md §14.2).
export const SCORING_CATEGORIES = [
	['Timeout handling', 20],
	['Retry behavior', 15],
	['Graceful degradation', 15],
	['Rate-limit handling', 10],
	['Idempotency', 10],
	['Authentication failure handling', 10],
	['Response validation', 10],
	['Dependency-failure handling', 10]
] as const;

// findingCategory → scoring category. Circuit breaker and fallback feed graceful degradation; Baseline isn't scored.
const scoringCategory: Record<string, string> = {
	Timeout: 'Timeout handling',
	Retry: 'Retry behavior',
	CircuitBreaker: 'Graceful degradation',
	Fallback: 'Graceful degradation',
	'Rate-limit': 'Rate-limit handling',
	Idempotency: 'Idempotency',
	'Auth failure': 'Authentication failure handling',
	'Response validation': 'Response validation',
	'Dependency failure': 'Dependency-failure handling'
};

// Per-outcome points (PROJECT.md §14.3); Inconclusive and Not applicable aren't scored.
const POINTS: Partial<Record<FindingOutcome, number>> = { Passed: 100, Degraded: 50, Failed: 0 };

export const isScored = (f: Pick<RunFinding, 'outcome'>) => POINTS[f.outcome] !== undefined;

export type CategoryScore = { label: string; weight: number; score: number | null; findings: number; inconclusive: number };

// A category score is the mean of its scored findings; null when it has none.
export function categoryScores(findings: RunFinding[]): CategoryScore[] {
	return SCORING_CATEGORIES.map(([label, weight]) => {
		const mine = findings.filter((f) => scoringCategory[findingCategory(f)] === label);
		const points = mine.flatMap((f) => POINTS[f.outcome] ?? []);
		return {
			label,
			weight,
			findings: mine.length,
			inconclusive: mine.filter((f) => f.outcome === 'Inconclusive').length,
			score: points.length ? points.reduce((a, b) => a + b, 0) / points.length : null
		};
	});
}

// Weighted mean of the scored categories; empty categories' weight is redistributed by leaving them out.
export function weightedScore(categories: CategoryScore[]): number | null {
	const scored = categories.filter((c) => c.score != null);
	const weight = scored.reduce((sum, c) => sum + c.weight, 0);
	return weight ? scored.reduce((sum, c) => sum + c.score! * c.weight, 0) / weight : null;
}

// The same method restricted to each endpoint, worst first; endpoints with no scored findings are left out.
export function endpointScores(findings: RunFinding[]) {
	const groups = Map.groupBy(findings, (f) => `${f.httpMethod} ${f.path}`);
	return [...groups.values()]
		.map((group) => ({ httpMethod: group[0].httpMethod, path: group[0].path, score: weightedScore(categoryScores(group)) }))
		.filter((e): e is { httpMethod: string; path: string; score: number } => e.score != null)
		.sort((a, b) => a.score - b.score);
}
