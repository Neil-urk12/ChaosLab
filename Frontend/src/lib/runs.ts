import type { FeedRow, RunHistoryRow, TestRunStatus } from './api';

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

// [label, chip classes] per run status.
export const statusStyles: Record<TestRunStatus, [string, string]> = {
	Queued: ['Queued', 'bg-sky-500/10 text-sky-300'],
	Running: ['Running', 'bg-accent-tint text-accent-ink'],
	Completed: ['Completed', 'bg-emerald-500/10 text-emerald-300'],
	PartiallyCompleted: ['Partially completed', 'bg-amber-500/10 text-amber-300'],
	Cancelled: ['Cancelled', 'bg-slate-500/10 text-slate-400'],
	// A failed run is a runner problem; target failures are findings, not run status.
	Failed: ['Failed · runner error', 'bg-rose-500/10 text-rose-300']
};

export type FeedResult = 'Running' | 'Passed' | 'Degraded' | 'Failed' | 'Inconclusive' | 'NotApplicable' | 'Evaluating' | 'RunnerError' | 'Skipped';

// A row's result in the live feed: the worst finding once evaluated, else the execution's own outcome.
// Runner errors and skips are ChaosLab's problems, never the target's.
export function feedResult(row: Pick<FeedRow, 'status' | 'outcome' | 'findings'>): FeedResult {
	if (row.status === 'Running' || row.status === 'Queued') return 'Running';
	if (row.status === 'Skipped') return 'Skipped';
	if (row.status === 'Failed') return 'RunnerError';
	return row.findings[0]?.outcome ?? row.outcome ?? 'Evaluating';
}

// [label, chip classes] per feed result.
export const feedResultStyles: Record<FeedResult, [string, string]> = {
	Running: ['Running', 'bg-accent-tint text-accent-ink'],
	Passed: ['Pass', 'bg-emerald-500/10 text-emerald-300'],
	Degraded: ['Degraded', 'bg-amber-500/10 text-amber-300'],
	Failed: ['Fail', 'bg-rose-500/10 text-rose-300'],
	Inconclusive: ['Inconclusive', 'bg-slate-500/10 text-slate-300'],
	NotApplicable: ['N/A', 'bg-slate-500/10 text-slate-400'],
	Evaluating: ['Evaluating', 'bg-slate-500/10 text-slate-400'],
	RunnerError: ['Runner error', 'bg-slate-500/10 text-slate-300'],
	Skipped: ['Skipped', 'bg-slate-500/10 text-slate-400']
};

// Linear estimate from the pace so far; null until one scenario has finished.
export function remainingMs(elapsedMs: number, done: number, total: number): number | null {
	if (done <= 0 || elapsedMs <= 0) return null;
	return Math.max(0, Math.round((elapsedMs / done) * (total - done)));
}

// "3m 05s", "42s".
export function clockDuration(ms: number) {
	const seconds = Math.max(0, Math.round(ms / 1000));
	const minutes = Math.floor(seconds / 60);
	return minutes ? `${minutes}m ${String(seconds % 60).padStart(2, '0')}s` : `${seconds}s`;
}
