import type { FindingOutcome, RunFinding } from './api';

// Pass beats Degraded beats Fail. Inconclusive and Not applicable were not judged, so they have no rank.
const rank: Partial<Record<FindingOutcome, number>> = { Failed: 0, Degraded: 1, Passed: 2 };
const failing = (f: RunFinding | null) => f?.outcome === 'Failed' || f?.outcome === 'Degraded';

// Scenario IDs change with every spec version, so the same check in two runs is matched by endpoint, fault and pattern.
const findingKey = (f: RunFinding) => `${f.httpMethod} ${f.path}|${f.scenarioType}|${f.pattern}`;

export type FindingChange = { key: string; before: RunFinding | null; after: RunFinding | null };

export type FindingChanges = {
	worse: FindingChange[];
	better: FindingChange[];
	stillFailing: FindingChange[];
	// Failed or degraded before, but Inconclusive, Not applicable or not run after: not fixed, just not judged.
	unjudged: FindingChange[];
};

function byKey(findings: RunFinding[]) {
	const map = new Map<string, RunFinding>();
	for (const f of findings) {
		const seen = map.get(findingKey(f));
		// One check judged twice keeps its worst judged outcome.
		if (!seen || (rank[f.outcome] ?? 3) < (rank[seen.outcome] ?? 3)) map.set(findingKey(f), f);
	}
	return map;
}

// Sorts the checks of two runs by how their outcome moved; unchanged passes are left out. The newer run's
// findings come first, in the severity order the API returns them.
export function compareFindings(before: RunFinding[], after: RunFinding[]): FindingChanges {
	const old = byKey(before);
	const now = byKey(after);
	const changes: FindingChanges = { worse: [], better: [], stillFailing: [], unjudged: [] };
	for (const key of new Set([...now.keys(), ...old.keys()])) {
		const change = { key, before: old.get(key) ?? null, after: now.get(key) ?? null };
		const from = change.before ? rank[change.before.outcome] : undefined;
		const to = change.after ? rank[change.after.outcome] : undefined;
		if (to != null && from != null) {
			if (to < from) changes.worse.push(change);
			else if (to > from) changes.better.push(change);
			else if (failing(change.after)) changes.stillFailing.push(change);
		} else if (to != null) {
			// Failing now, and not judged (or not run) before: a new failure.
			if (failing(change.after)) changes.worse.push(change);
		} else if (failing(change.before)) changes.unjudged.push(change);
	}
	return changes;
}

// Rows of two runs' stored scores matched by key, in the newer run's order, then rows only the older run has.
export function pairScores<T>(before: T[], after: T[], keyOf: (row: T) => string) {
	const old = new Map(before.map((row) => [keyOf(row), row]));
	const now = new Map(after.map((row) => [keyOf(row), row]));
	return [...new Set([...now.keys(), ...old.keys()])].map((key) => ({ key, before: old.get(key) ?? null, after: now.get(key) ?? null }));
}

// Change between two stored scores, rounded as the results page shows it; null when either is missing.
export const scoreChange = (before: number | null | undefined, after: number | null | undefined) =>
	before == null || after == null ? null : Math.round(after - before);
