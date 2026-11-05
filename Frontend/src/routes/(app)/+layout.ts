import { error, redirect } from '@sveltejs/kit';

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

export async function load({ fetch }) {
	const res = await fetch('/api/auth/me');
	if (res.status === 401 || res.status === 403) redirect(307, '/login');
	if (!res.ok) error(res.status, 'Could not load your profile.');
	const me: CurrentUser = await res.json();
	if (me.mustChangePassword) redirect(307, '/setup');
	return { me };
}
