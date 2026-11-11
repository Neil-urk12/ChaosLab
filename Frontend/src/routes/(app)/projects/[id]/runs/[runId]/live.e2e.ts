import { expect, test, type Page } from '@playwright/test';

const origin = 'http://localhost:4173';

const row = (id: string, scenarioType: string, httpMethod: string, path: string, status: string, extra: Record<string, unknown> = {}) => ({
	executionId: id, httpMethod, path, scenarioType, status, outcome: null, failureReason: null,
	startedAt: '2026-11-08T08:14:00Z', completedAt: status === 'Running' ? null : '2026-11-08T08:14:01.200Z', findings: [], ...extra
});

const runningRun = {
	id: 'r27', number: 27, status: 'Running', environmentName: 'Staging', requestedBy: 'Maya Chen',
	createdAt: '2026-11-08T08:12:00Z', startedAt: '2026-11-08T08:12:00Z', completedAt: null,
	completedScenarios: 4, totalScenarios: 96, skippedScenarios: 1, score: null, scoringVersion: '1.0',
	failureMessage: null, previous: null, timelines: [],
	live: {
		feed: [
			row('e5', 'Latency', 'GET', '/payments/{id}', 'Running'),
			row('e4', 'MalformedJson', 'POST', '/refunds', 'Completed', {
				outcome: 'Failed', findings: [{ outcome: 'Failed', summary: 'Unhandled 500 with stack trace in body' }]
			}),
			row('e3', 'Throttle429', 'GET', '/payments', 'Completed', {
				outcome: 'Passed', findings: [{ outcome: 'Degraded', summary: 'Retry-After ignored on 1 of 3 throttles' }]
			}),
			row('e2', 'TimeoutDrop', 'POST', '/payments/{id}/capture', 'Failed', { failureReason: 'Injector lost the connection.' }),
			row('e1', 'DnsFailure', 'POST', '/payments', 'Skipped', { failureReason: "Simulator not in this target's outbound path" })
		],
		current: {
			executionId: 'e5',
			parameters: { delayMs: 3000 },
			invariants: [{ type: 'MaxDurationMs', params: { ms: 5000 } }],
			events: [
				{ attemptNo: 1, requestMethod: 'GET', requestPath: '/payments/{id}', startedAt: '2026-11-08T08:15:12Z', durationMs: 3000, clientStatusCode: null, upstreamStatusCode: null, outcomeType: 'Timeout', errorType: null },
				{ attemptNo: 2, requestMethod: 'GET', requestPath: '/payments/{id}', startedAt: '2026-11-08T08:15:15Z', durationMs: null, clientStatusCode: null, upstreamStatusCode: null, outcomeType: 'Forwarded', errorType: null }
			]
		}
	}
};

// Maya is an owner of project "a"; run r27 is running until it is cancelled.
async function mockRun(page: Page) {
	const state = { cancelled: 0 };
	await page.route('**/api/auth/me', (route) => route.fulfill({
		json: { id: 'maya', userName: 'maya', displayName: 'Maya Chen', email: null, mustChangePassword: false, roles: [] }
	}));
	await page.route('**/api/projects/overview', (route) => route.fulfill({
		json: {
			projectCount: 1, endpointCount: 0, runsLast7Days: 0, runsPrevious7Days: 0, averageScore: null, previousAverageScore: null,
			scoredRunsLast30Days: 0, failedFindingCount: 0, scoreTrend: [], findings: [], recentRuns: [], recentProjects: []
		}
	}));
	await page.route(/\/api\/projects\/a$/, (route) => route.fulfill({
		json: {
			id: 'a', name: 'Payment Gateway', description: null, defaultEnvironment: null, endpointCount: 0, scenarioCount: 0,
			completedRunCount: 0, failedFindingCount: 0, latest: null, spec: null, findings: [], recentRuns: [],
			members: [{ userId: 'maya', displayName: 'Maya Chen', role: 'Owner' }]
		}
	}));
	await page.route(/\/api\/projects\/a\/findings/, (route) => route.fulfill({ status: 404 }));
	await page.route(/\/api\/projects\/a\/runs\/r27\/cancel$/, (route) => {
		state.cancelled++;
		return route.fulfill({ status: 204 });
	});
	await page.route(/\/api\/projects\/a\/runs\/r27$/, (route) => route.fulfill({
		json: state.cancelled ? { ...runningRun, status: 'Cancelled', completedAt: '2026-11-08T08:16:00Z', live: null } : runningRun
	}));
	return state;
}

test('running run shows progress, counts, the feed and the scenario in flight', async ({ page }) => {
	await mockRun(page);
	await page.goto(`${origin}/projects/a/runs/r27`);

	await expect(page.getByRole('heading', { name: 'RUN-027', exact: true })).toBeVisible();
	await expect(page.getByRole('progressbar', { name: 'Scenarios completed' })).toHaveAttribute('aria-valuenow', '4');
	await expect(page.getByText('Degraded', { exact: true }).first()).toBeVisible();

	const nowRunning = page.getByRole('region', { name: 'Now running' });
	await expect(nowRunning.getByText('+3000 ms')).toBeVisible();
	await expect(nowRunning.getByText('Attempt 2', { exact: true }).first()).toBeVisible();
	await expect(page.getByText('Attempt 2 in flight')).toBeVisible();

	await expect(page.locator('tbody tr')).toHaveCount(5);
	await page.getByRole('button', { name: 'Problems' }).click();
	await expect(page.locator('tbody tr')).toHaveCount(2);
	await page.getByRole('button', { name: 'Runner errors' }).click();
	await expect(page.locator('tbody tr')).toHaveCount(1);
	await expect(page.getByText('Injector lost the connection.')).toBeVisible();
});

test('cancelling a run switches to its results page', async ({ page }) => {
	const state = await mockRun(page);
	await page.goto(`${origin}/projects/a/runs/r27`);
	page.on('dialog', (dialog) => dialog.accept());
	await page.getByRole('button', { name: 'Cancel run' }).click();
	await expect(page.getByRole('heading', { name: 'RUN-027 results' })).toBeVisible();
	expect(state.cancelled).toBe(1);
});

test('cancelling a run that just finished switches to its results page without waiting for a reload', async ({ page }) => {
	const state = await mockRun(page);
	await page.goto(`${origin}/projects/a/runs/r27`);
	await page.route(/\/api\/projects\/a\/runs\/r27\/cancel$/, (route) => {
		state.cancelled++;
		return route.fulfill({ status: 409, json: 'This run has already finished.' });
	});
	page.on('dialog', (dialog) => dialog.accept());
	await page.getByRole('button', { name: 'Cancel run' }).click();
	await expect(page.getByRole('heading', { name: 'RUN-027 results' })).toBeVisible({ timeout: 2000 });
});

test('a failed reload keeps the live view and the next one updates it', async ({ page }) => {
	await mockRun(page);
	const errors: Error[] = [];
	page.on('pageerror', (error) => errors.push(error));
	let findings = 0;
	page.on('request', (request) => void (/\/findings/.test(request.url()) && findings++));
	await page.goto(`${origin}/projects/a/runs/r27`);
	const progress = page.getByRole('progressbar', { name: 'Scenarios completed' });
	await expect(progress).toHaveAttribute('aria-valuenow', '4');

	// First reload: 502. Second: a 200 that isn't JSON (a proxy's HTML page). Third: the run.
	const bad = [{ status: 502 }, { status: 200, contentType: 'text/html', body: '<html>' }];
	let failed = 0;
	await page.route(/\/api\/projects\/a\/runs\/r27$/, (route) =>
		route.fulfill(bad[failed++] ?? { json: { ...runningRun, completedScenarios: 5 } })
	);
	await expect(progress).toHaveAttribute('aria-valuenow', '5', { timeout: 15_000 });
	expect(failed).toBeGreaterThan(2);
	expect(errors).toEqual([]);
	// Findings load once; they only change when the run finishes.
	expect(findings).toBe(1);
});

test('a reload that never answers is given up on and the next one updates the view', async ({ page }) => {
	await mockRun(page);
	await page.goto(`${origin}/projects/a/runs/r27`);
	const progress = page.getByRole('progressbar', { name: 'Scenarios completed' });
	await expect(progress).toHaveAttribute('aria-valuenow', '4');

	let polls = 0;
	await page.route(/\/api\/projects\/a\/runs\/r27$/, (route) =>
		polls++ ? route.fulfill({ json: { ...runningRun, completedScenarios: 5 } }) : undefined
	);
	await expect(progress).toHaveAttribute('aria-valuenow', '5', { timeout: 20_000 });
});
