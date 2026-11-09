import { describe, expect, it } from 'vitest';
import { compareProblem, dayRange, DEFAULT_DAYS, pageWindow, runLabel } from './runs';

const run = (environmentName = 'Staging', scoringVersion = '1.0') => ({ environmentName, scoringVersion });

describe('runs', () => {
	it('pads run numbers', () => {
		expect(runLabel({ number: 27 })).toBe('RUN-027');
		expect(runLabel({ number: 1234 })).toBe('RUN-1234');
	});

	it('keeps only date ranges the menu offers', () => {
		expect(dayRange('7')).toBe('7');
		expect(dayRange('all')).toBe('all');
		expect(dayRange('14')).toBe(DEFAULT_DAYS);
		expect(dayRange(null)).toBe(DEFAULT_DAYS);
	});

	it('shows first, last and nearby pages with gaps', () => {
		expect(pageWindow(1, 1)).toEqual([1]);
		expect(pageWindow(2, 5)).toEqual([1, 2, 3, 4, 5]);
		expect(pageWindow(1, 100)).toEqual([1, 2, 3, null, 100]);
		expect(pageWindow(50, 100)).toEqual([1, null, 48, 49, 50, 51, 52, null, 100]);
		expect(pageWindow(100, 100)).toEqual([1, null, 98, 99, 100]);
	});

	it('only compares two runs on the same environment and scoring version', () => {
		expect(compareProblem([run(), run()])).toBeNull();
		expect(compareProblem([run()])).toMatch(/exactly two/);
		expect(compareProblem([run(), run('Prod-like test')])).toMatch(/environments/);
		expect(compareProblem([run(), run('Staging', '2.0')])).toMatch(/scoring versions/);
	});
});
