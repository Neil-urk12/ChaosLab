export const PLACEHOLDER = '—';

export const score = (value: number | null | undefined) =>
	value == null ? PLACEHOLDER : String(Math.round(value));

export const initials = (name: string) =>
	name
		.split(/\s+/)
		.filter(Boolean)
		.slice(0, 2)
		.map((word) => word[0].toUpperCase())
		.join('');

export const scoreLabel = (value: number) => (value >= 80 ? 'Good' : value >= 60 ? 'Fair' : 'Poor');

export function duration(start: string | null, end: string | null) {
	if (!start || !end) return PLACEHOLDER;
	const seconds = Math.max(0, Math.round((Date.parse(end) - Date.parse(start)) / 1000));
	const minutes = Math.floor(seconds / 60);
	return minutes ? `${minutes}m ${seconds % 60}s` : `${seconds}s`;
}

const time = (date: Date) => date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', hour12: false });

export const dateTime = (iso: string) => {
	const date = new Date(iso);
	return `${date.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })} · ${time(date)}`;
};

// "12 min ago", "Today, 09:42", "Yesterday, 18:06", then a full date.
export function relative(iso: string, now = new Date()) {
	const date = new Date(iso);
	const minutes = Math.floor((now.getTime() - date.getTime()) / 60000);
	if (minutes >= 0 && minutes < 60) return minutes < 1 ? 'Just now' : `${minutes} min ago`;
	const days = Math.round(
		(new Date(now).setHours(0, 0, 0, 0) - new Date(date).setHours(0, 0, 0, 0)) / 86400000
	);
	if (days === 0) return `Today, ${time(date)}`;
	if (days === 1) return `Yesterday, ${time(date)}`;
	return dateTime(iso);
}

// SVG line and filled-area paths for 0–100 scores, scaled to the data range so small changes stay visible.
export function trendPaths(values: number[], width: number, height: number) {
	if (!values.length) return { line: '', area: '' };
	const low = Math.max(0, Math.min(...values) - 5);
	const high = Math.min(100, Math.max(...values) + 5);
	const y = (v: number) => +(height - ((v - low) / (high - low || 1)) * height).toFixed(1);
	const points = (values.length === 1 ? [values[0], values[0]] : values).map(
		(v, i, all) => `${+((i / (all.length - 1)) * width).toFixed(1)},${y(v)}`
	);
	const line = `M${points.join(' L')}`;
	return { line, area: `${line} L${width},${height} L0,${height} Z` };
}
