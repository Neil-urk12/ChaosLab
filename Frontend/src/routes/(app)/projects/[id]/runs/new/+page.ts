import { error } from '@sveltejs/kit';
import type { ScenarioList } from '$lib/api';

// The preflight probes the target and can take up to 30 s, so the page fetches it itself.
export async function load({ fetch, params }) {
	const res = await fetch(`/api/projects/${encodeURIComponent(params.id)}/scenarios`);
	// 404 means no spec has been imported yet; the layout already 404s for unknown projects.
	if (res.status === 404) return { list: null };
	if (!res.ok) error(res.status, 'Could not load scenarios.');
	const list: ScenarioList = await res.json();
	return { list };
}
