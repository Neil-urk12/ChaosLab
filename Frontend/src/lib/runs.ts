import type { RunHistoryRow } from './api';

// Date range used when the URL names none.
export const DEFAULT_DAYS = '30';

export const DAY_RANGES = [
	['7', 'Last 7 days'],
	['30', 'Last 30 days'],
	['90', 'Last 90 days'],
	['all', 'All time']
] as const;

// A days value outside the menu would filter while the menu showed nothing, so it falls back to the default.
export const dayRange = (raw: string | null): string => (DAY_RANGES.some(([value]) => value === raw) ? raw! : DEFAULT_DAYS);

// Page numbers for the pager: first, last and current ±2; null marks a gap. Keeps the pager narrow however many pages there are.
export function pageWindow(current: number, count: number): (number | null)[] {
	const items: (number | null)[] = [];
	for (let n = 1; n <= count; n++) {
		if (n === 1 || n === count || Math.abs(n - current) <= 2) items.push(n);
		else if (items.at(-1) !== null) items.push(null);
	}
	return items;
}

export const runLabel = (run: Pick<RunHistoryRow, 'number'>) => `RUN-${String(run.number).padStart(3, '0')}`;

// Two runs compare meaningfully only on the same environment and scoring version.
export function compareProblem(runs: Pick<RunHistoryRow, 'environmentName' | 'scoringVersion'>[]) {
	if (runs.length !== 2) return 'Select exactly two runs to compare';
	const [a, b] = runs;
	if (a.environmentName !== b.environmentName) return 'Runs use different environments';
	if (a.scoringVersion !== b.scoringVersion) return 'Runs use different scoring versions';
	return null;
}
