import { error } from '@sveltejs/kit';

export type Project = {
	id: string;
	name: string;
	description: string | null;
	createdAt: string;
	updatedAt: string;
};

export async function load({ fetch }) {
	const res = await fetch('/api/projects');
	if (!res.ok) error(res.status, 'Could not load projects.');
	const projects: Project[] = await res.json();
	return { projects };
}
