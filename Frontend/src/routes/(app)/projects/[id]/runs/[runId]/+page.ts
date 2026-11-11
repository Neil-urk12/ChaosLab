import { error } from '@sveltejs/kit';
import type { RunDetail, RunFinding } from '$lib/api';

export async function load({ fetch, params }) {
	const base = `/api/projects/${encodeURIComponent(params.id)}`;
	const runId = encodeURIComponent(params.runId);
	const [runRes, findingsRes] = await Promise.all([fetch(`${base}/runs/${runId}`), fetch(`${base}/findings?run=${runId}`)]);
	if (runRes.status === 400 || runRes.status === 404) error(404, 'Run not found.');
	if (!runRes.ok) error(runRes.status, 'Could not load this run.');
	// The findings query answers 404 for a run with no findings yet.
	if (!findingsRes.ok && findingsRes.status !== 404) error(findingsRes.status, 'Could not load findings.');
	const run: RunDetail = await runRes.json();
	const findings: RunFinding[] = findingsRes.ok ? (await findingsRes.json()).findings : [];
	return { run, findings };
}
