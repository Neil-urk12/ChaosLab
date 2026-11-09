import { error } from '@sveltejs/kit';
import type { RunFindings } from '$lib/api';

// ?run= picks the run; without it the server picks the newest completed run with findings.
export async function load({ fetch, params, url }) {
	const run = url.searchParams.get('run');
	const query = run ? `?run=${encodeURIComponent(run)}` : '';
	const res = await fetch(`/api/projects/${encodeURIComponent(params.id)}/findings${query}`);
	if (res.status === 400 || (run && res.status === 404)) error(404, 'Run not found.');
	if (!res.ok) error(res.status, 'Could not load findings.');
	const findings: RunFindings = await res.json();
	return { findings };
}
