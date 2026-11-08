import { error } from '@sveltejs/kit';
import type { ApiSpecDetail } from '$lib/api';

export async function load({ fetch, params }) {
	const res = await fetch(`/api/projects/${encodeURIComponent(params.id)}/spec`);
	// 404 also means no spec has been imported yet; the layout already 404s for unknown projects.
	if (res.status === 404) return { spec: null };
	if (!res.ok) error(res.status, 'Could not load the API specification.');
	const spec: ApiSpecDetail = await res.json();
	return { spec };
}
