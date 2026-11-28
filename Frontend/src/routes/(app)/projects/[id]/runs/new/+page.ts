import { error } from '@sveltejs/kit';
import type { RunFindings, ScenarioList } from '$lib/api';
import { rerunScenarioIds } from '$lib/runs';

// The preflight probes the target and can take up to 30 s, so the page fetches it itself.
export async function load({ fetch, params, url }) {
	const base = `/api/projects/${encodeURIComponent(params.id)}`;
	// "Rerun failed" names a run; only a GUID is forwarded, as on the run results page.
	const rerun = url.searchParams.get('rerun') ?? '';
	const [res, rerunRes] = await Promise.all([
		fetch(`${base}/scenarios`),
		/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(rerun) ? fetch(`${base}/findings?run=${rerun}`) : null
	]);
	// 404 means no spec has been imported yet; the layout already 404s for unknown projects.
	if (res.status === 404) return { list: null, rerun: null };
	if (!res.ok) error(res.status, 'Could not load scenarios.');
	const list: ScenarioList = await res.json();
	// Failing silently would start the default selection instead of the rerun, so a bad link is an error.
	if (rerunRes && !rerunRes.ok) error(rerunRes.status, 'Could not load the run to rerun.');
	const findings: RunFindings | null = rerunRes ? await rerunRes.json() : null;
	return { list, rerun: findings?.run ? { run: findings.run, ids: rerunScenarioIds(findings.findings) } : null };
}
