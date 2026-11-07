import { describe, expect, it } from 'vitest';
import { duration, relative, score, trendPaths } from './format';

describe('format', () => {
	it('rounds scores and shows a placeholder for missing ones', () => {
		expect(score(77.5)).toBe('78');
		expect(score(null)).toBe('—');
	});

	it('formats run durations', () => {
		expect(duration('2026-10-04T14:00:00Z', '2026-10-04T14:06:42Z')).toBe('6m 42s');
		expect(duration('2026-10-04T14:00:00Z', '2026-10-04T14:00:09Z')).toBe('9s');
		expect(duration('2026-10-04T14:00:00Z', null)).toBe('—');
	});

	it('describes recent times relative to now', () => {
		const now = new Date(2026, 9, 4, 14, 30);
		expect(relative(new Date(2026, 9, 4, 14, 18).toISOString(), now)).toBe('12 min ago');
		expect(relative(new Date(2026, 9, 4, 9, 42).toISOString(), now)).toMatch(/^Today, /);
		expect(relative(new Date(2026, 9, 3, 18, 6).toISOString(), now)).toMatch(/^Yesterday, /);
		expect(relative(new Date(2026, 8, 20).toISOString(), now)).toContain('2026');
	});

	it('builds trend paths that span the chart', () => {
		expect(trendPaths([], 100, 50)).toEqual({ line: '', area: '' });
		const { line, area } = trendPaths([70, 80], 100, 50);
		expect(line).toBe('M0,37.5 L100,12.5');
		expect(area).toBe('M0,37.5 L100,12.5 L100,50 L0,50 Z');
		expect(trendPaths([75], 100, 50).line).toBe('M0,25 L100,25');
	});
});
