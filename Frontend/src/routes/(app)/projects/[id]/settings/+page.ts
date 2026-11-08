import { error } from '@sveltejs/kit';
import type { EnvironmentSettings } from '$lib/api';

export async function load({ fetch, params }) {
	const res = await fetch(`/api/projects/${encodeURIComponent(params.id)}/environment`);
	if (!res.ok) error(res.status, 'Could not load project settings.');
	const settings: EnvironmentSettings = await res.json();
	return { settings };
}
