import { expect, test, type Page } from '@playwright/test';

const origin = 'http://localhost:4173';

const scenario = (id: string, endpointId: string, httpMethod: string, path: string, type: string, enabled = true) => ({
	id, endpointId, httpMethod, path, operationId: null, type, parameters: {}, reason: '', enabled, invariants: []
});

const scenarios = [
	scenario('s1', 'e1', 'GET', '/payments', 'Latency'),
	scenario('s2', 'e1', 'GET', '/payments', 'SlowDrip', false),
	scenario('s3', 'e2', 'POST', '/payments', 'Latency'),
	scenario('s4', 'e2', 'POST', '/payments', 'DuplicateRequest')
];

const preflight = (blockers: string[] = []) => ({
	target: { name: 'Staging', baseUrl: 'https://api.test', kind: 'Staging', authorized: true },
	reachability: blockers.length ? { reachable: false, status: null, error: 'No response within 30 s.', elapsedMs: 30000 } : { reachable: true, status: 200, error: null, elapsedMs: 42 },
	credentials: [],
	spec: { openApiVersion: '3.0.3', operationCount: 3, neverTestCount: 1 },
	scoringVersion: '1.0',
	blockers
});

async function mockApi(page: Page, blockers: string[] = []) {
	const posted: unknown[] = [];
	await page.route('**/api/auth/me', (route) => route.fulfill({
		json: { id: 'me', userName: 'me', displayName: 'Maya', email: null, mustChangePassword: false, roles: [] }
	}));
	await page.route('**/api/projects/overview', (route) => route.fulfill({
		json: {
			projectCount: 1, endpointCount: 0, runsLast7Days: 0, runsPrevious7Days: 0, averageScore: null, previousAverageScore: null,
			scoredRunsLast30Days: 0, failedFindingCount: 0, scoreTrend: [], findings: [], recentRuns: [], recentProjects: []
		}
	}));
	await page.route(/\/api\/projects\/p(\/|$)/, (route) => {
		const path = new URL(route.request().url()).pathname;
		if (path === '/api/projects/p')
			return route.fulfill({
				json: {
					id: 'p', name: 'Payments', description: null, defaultEnvironment: { name: 'Staging', baseUrl: 'https://api.test' },
					endpointCount: 2, scenarioCount: 4, completedRunCount: 0, failedFindingCount: 0, latest: null, spec: null,
					members: [{ userId: 'me', displayName: 'Maya', role: 'Owner' }], findings: [], recentRuns: []
				}
			});
		if (path === '/api/projects/p/scenarios') return route.fulfill({ json: { endpointCount: 2, scenarios } });
		if (path === '/api/projects/p/runs/preflight') return route.fulfill({ json: preflight(blockers) });
		if (path === '/api/projects/p/runs' && route.request().method() === 'POST') {
			posted.push(route.request().postDataJSON());
			return route.fulfill({ status: 201, json: { id: 'r1' } });
		}
		return route.fulfill({ status: 404, json: null });
	});
	return posted;
}

test('starts a run with the chosen scenarios after confirming state-changing ones', async ({ page }) => {
	const posted = await mockApi(page);
	await page.goto(`${origin}/projects/p/runs/new`);
	await expect(page.getByText('Target reachable')).toBeVisible();
	await expect(page.getByText('"Never test" operation is hidden')).toBeVisible();

	const start = page.getByRole('button', { name: 'Start run' });
	await expect(start).toBeDisabled();
	await page.getByRole('checkbox', { name: /I understand/ }).check();
	await expect(start).toBeEnabled();

	// Deselecting GET /payments leaves only the POST scenarios.
	await page.getByRole('listitem').filter({ hasText: 'GET' }).filter({ hasText: '/payments' }).first().getByRole('checkbox').uncheck();
	await page.getByRole('radio', { name: '3' }).click();
	await start.click();

	await expect(page).toHaveURL(`${origin}/projects/p/runs/r1`);
	expect(posted).toEqual([{ scenarioIds: ['s3', 's4'], concurrency: 3, baselineRequests: true, runnerRetries: 2, confirmStateChanging: true }]);
});

test('a preflight blocker keeps the run from starting', async ({ page }) => {
	await mockApi(page, ['The target is unreachable: No response within 30 s.']);
	await page.goto(`${origin}/projects/p/runs/new`);
	await expect(page.getByRole('alert')).toContainText('The target is unreachable');
	await page.getByRole('checkbox', { name: /I understand/ }).check();
	await expect(page.getByRole('button', { name: 'Start run' })).toBeDisabled();
});
