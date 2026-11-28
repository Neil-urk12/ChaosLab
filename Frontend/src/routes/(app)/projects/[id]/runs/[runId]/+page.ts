import { error } from '@sveltejs/kit';
import type { ReportRow, RunDetail, RunFinding } from '$lib/api';

export async function load({ fetch, params, url }) {
	const base = `/api/projects/${encodeURIComponent(params.id)}`;
	const runId = encodeURIComponent(params.runId);
	// A finding's "View in timeline" link names its scenario, which may fall outside the run's worst timelines.
	// Only a GUID is forwarded: the API answers anything else with 400, which would show a run that exists as not found.
	const timeline = url.searchParams.get('timeline') ?? '';
	const timelineQuery = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(timeline) ? `?timeline=${timeline}` : '';
	const [runRes, findingsRes, reportsRes] = await Promise.all([
		fetch(`${base}/runs/${runId}${timelineQuery}`),
		fetch(`${base}/findings?run=${runId}`),
		// The report list is secondary: if it fails, the results still show and the list is empty.
		fetch(`${base}/runs/${runId}/reports`).catch(() => null)
	]);
	if (runRes.status === 400 || runRes.status === 404) error(404, 'Run not found.');
	if (!runRes.ok) error(runRes.status, 'Could not load this run.');
	// The findings query answers 404 for a run with no findings yet.
	if (!findingsRes.ok && findingsRes.status !== 404) error(findingsRes.status, 'Could not load findings.');
	const run: RunDetail = await runRes.json();
	const findings: RunFinding[] = findingsRes.ok ? (await findingsRes.json()).findings : [];
	const reports: ReportRow[] = reportsRes?.ok ? await reportsRes.json().catch(() => []) : [];
	return { run, findings, reports };
}
