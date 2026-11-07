import { error } from '@sveltejs/kit';
import type { ProjectListRow } from '$lib/api';

export async function load({ fetch }) {
	const res = await fetch('/api/projects');
	if (!res.ok) error(res.status, 'Could not load your projects.');
	const projects: ProjectListRow[] = await res.json();
	return { projects };
}
