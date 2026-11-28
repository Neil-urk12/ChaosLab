import { error } from '@sveltejs/kit';
import type { RunDetail, RunFinding } from '$lib/api';
import { isGuid } from '$lib/runs';

// Two runs side by side (FeaturesFlow 2.23), older first, from the queries the run results page uses.
export async function load({ fetch, params, url }) {
	const base = `/api/projects/${encodeURIComponent(params.id)}`;
	const ids = [url.searchParams.get('a'), url.searchParams.get('b')];
	if (!ids.every(isGuid)) error(404, 'Select two runs to compare.');
	const loaded = await Promise.all(
		ids.map(async (id) => {
			const [runRes, findingsRes] = await Promise.all([fetch(`${base}/runs/${id}`), fetch(`${base}/findings?run=${id}`)]);
			if (runRes.status === 404) error(404, 'Run not found.');
			if (!runRes.ok) error(runRes.status, 'Could not load this run.');
			// The findings query answers 404 for a run with no findings yet.
			if (!findingsRes.ok && findingsRes.status !== 404) error(findingsRes.status, 'Could not load findings.');
			const run: RunDetail = await runRes.json();
			const findings: RunFinding[] = findingsRes.ok ? (await findingsRes.json()).findings : [];
			return { run, findings };
		})
	);
	const [before, after] = loaded.sort((x, y) => x.run.number - y.run.number);
	return { before, after };
}
