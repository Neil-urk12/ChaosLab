import { error } from '@sveltejs/kit';
import type { RunHistoryPage } from '$lib/api';
import { dayRange } from '$lib/runs';

// The URL holds the filters: ?query=&status=&environment=&days=(7|30|90|all)&page=. No or unknown days means the last 30 days.
export async function load({ fetch, params, url }) {
	const search = url.searchParams;
	const api = new URLSearchParams();
	for (const key of ['query', 'status', 'environment', 'page']) {
		const value = search.get(key);
		if (value) api.set(key, value);
	}
	const days = dayRange(search.get('days'));
	if (days !== 'all') api.set('days', days);

	const res = await fetch(`/api/projects/${encodeURIComponent(params.id)}/runs?${api}`);
	if (res.status === 400) error(400, 'Invalid test run filters.');
	if (!res.ok) error(res.status, 'Could not load test runs.');
	const history: RunHistoryPage = await res.json();
	return { history, days };
}
