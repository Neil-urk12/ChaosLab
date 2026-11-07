import { error } from '@sveltejs/kit';
import type { ProjectDetail } from '$lib/api';

export async function load({ fetch, params }) {
	const res = await fetch(`/api/projects/${encodeURIComponent(params.id)}`);
	// The API returns 404 for unknown ids and for projects the caller is not a member of.
	if (res.status === 404) error(404, 'Project not found.');
	if (!res.ok) error(res.status, 'Could not load this project.');
	const project: ProjectDetail = await res.json();
	return { project };
}
