import { error } from '@sveltejs/kit';
import type { ProjectMembers } from '$lib/api';

export async function load({ fetch, params }) {
	const res = await fetch(`/api/projects/${encodeURIComponent(params.id)}/members`);
	if (!res.ok) error(res.status, 'Could not load the team.');
	const team: ProjectMembers = await res.json();
	return { team };
}
