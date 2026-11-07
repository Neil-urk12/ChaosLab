import { error, redirect } from '@sveltejs/kit';
import type { Overview } from '$lib/api';

// The auth cookie only reaches /api through the browser (dev proxy), so load client-side.
export const ssr = false;

export type CurrentUser = {
	id: string;
	userName: string;
	displayName: string;
	email: string | null;
	mustChangePassword: boolean;
	roles: string[];
};

// The overview feeds both the sidebar badges and the Overview page; call invalidateAll() after mutations.
export async function load({ fetch }) {
	const [meRes, overviewRes] = await Promise.all([fetch('/api/auth/me'), fetch('/api/projects/overview')]);
	if (meRes.status === 401 || meRes.status === 403) redirect(307, '/login');
	if (!meRes.ok) error(meRes.status, 'Could not load your profile.');
	const me: CurrentUser = await meRes.json();
	if (me.mustChangePassword) redirect(307, '/setup');
	if (!overviewRes.ok) error(overviewRes.status, 'Could not load your workspace.');
	const overview: Overview = await overviewRes.json();
	return { me, overview };
}
