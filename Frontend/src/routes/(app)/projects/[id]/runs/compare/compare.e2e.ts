import { expect, test, type Page } from '@playwright/test';

const origin = 'http://localhost:4173';
const id = (n: number) => `00000000-0000-4000-8000-00000000000${n}`;

const detail = (n: number, score: number, environmentName = 'Staging', status = 'Completed') => ({
	id: id(n), number: n, status, environmentName, requestedBy: 'Maya',
	createdAt: '2026-11-01T00:00:00Z', startedAt: '2026-11-01T00:00:00Z', completedAt: '2026-11-01T00:01:00Z',
	completedScenarios: 3, totalScenarios: 3, skippedScenarios: 0, score, evidenceCoverage: 100,
	categories: [{ category: 'Retry', weight: 15, score, findings: 2, inconclusive: 0 }],
	endpoints: [{ httpMethod: 'GET', path: '/orders', score }, ...(n === 1 ? [{ httpMethod: 'GET', path: '/legacy', score: 100 }] : [])],
	scoringVersion: '1.0', failureMessage: null, previous: null, timelines: [], problems: [], live: null
});

const finding = (outcome: string, path: string, scenarioType: string, summary: string) => ({
	id: `${path}-${scenarioType}-${outcome}`, severity: 'High', outcome, pattern: 'Retry', category: 'Retry', httpMethod: 'GET', path,
	executionId: null, scenarioId: null, scenarioType, summary, evidence: {}, recommendation: null
});

const findings: Record<string, unknown[]> = {
	[id(1)]: [finding('Failed', '/orders', 'ServerErrorStorm', 'Gave up on the storm'), finding('Passed', '/orders', 'Throttle429', 'Waited')],
	[id(2)]: [finding('Passed', '/orders', 'ServerErrorStorm', 'Retried through the storm'), finding('Degraded', '/orders', 'Throttle429', 'Ignored Retry-After')]
};

async function mockApi(page: Page, environment = 'Staging', status = 'Completed') {
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
		const url = new URL(route.request().url());
		const path = url.pathname;
		if (path === '/api/projects/p')
			return route.fulfill({
				json: {
					id: 'p', name: 'Orders', description: null, defaultEnvironment: null, endpointCount: 1, scenarioCount: 2, completedRunCount: 2,
					failedFindingCount: 1, latest: null, spec: null, members: [{ userId: 'me', displayName: 'Maya', role: 'Owner' }], findings: [], recentRuns: []
				}
			});
		if (path === '/api/projects/p/runs') {
			const row = (n: number, score: number) => ({
				...detail(n, score), failedScenarios: 0, previousScore: null
			});
			return route.fulfill({
				json: { total: 2, matched: 2, page: 1, pageSize: 20, environments: ['Staging'], scoringVersions: ['1.0'], runs: [row(2, 75), row(1, 60)] }
			});
		}
		if (path === `/api/projects/p/runs/${id(1)}`) return route.fulfill({ json: detail(1, 60) });
		if (path === `/api/projects/p/runs/${id(2)}`) return route.fulfill({ json: detail(2, 75, environment, status) });
		if (path === '/api/projects/p/findings') {
			const run = url.searchParams.get('run')!;
			return route.fulfill({ json: { runs: [], run: { id: run, number: 1, status: 'Completed', completedAt: null }, findings: findings[run] } });
		}
		return route.fulfill({ status: 404, json: null });
	});
}

test('two selected runs open side by side, older first, with what changed', async ({ page }) => {
	await mockApi(page);
	await page.goto(`${origin}/projects/p/runs`);
	await page.getByRole('checkbox', { name: 'Select RUN-002' }).check();
	await page.getByRole('checkbox', { name: 'Select RUN-001' }).check();
	await page.getByRole('link', { name: 'Compare runs' }).click();

	await expect(page).toHaveURL(`${origin}/projects/p/runs/compare?a=${id(2)}&b=${id(1)}`);
	await expect(page.getByRole('heading', { name: 'RUN-001 vs RUN-002' })).toBeVisible();
	await expect(page.getByLabel('Score change')).toContainText('+15');

	const changes = page.getByRole('region', { name: 'Finding changes' });
	await expect(changes.getByRole('heading', { name: 'Worse (1)' })).toBeVisible();
	await expect(changes.getByRole('listitem').filter({ hasText: 'Ignored Retry-After' })).toContainText('Pass → Degraded');
	await expect(changes.getByRole('heading', { name: 'Better (1)' })).toBeVisible();
	await expect(changes.getByRole('listitem').filter({ hasText: 'Retried through the storm' })).toContainText('Fail → Pass');
	await expect(changes.getByRole('heading', { name: /Still failing|Not judged again/ })).toHaveCount(0);

	await expect(page.getByRole('region', { name: 'Category scores' }).getByRole('row', { name: 'Retry behavior 15% 60 75 +15' })).toBeVisible();
	// An endpoint only the earlier run scored has no change and sorts last.
	const endpointRows = page.getByRole('region', { name: 'Endpoint scores' }).getByRole('row');
	await expect(endpointRows.nth(1)).toContainText('/orders');
	await expect(endpointRows.nth(2)).toContainText(/\/legacy\s*100\s*—\s*—/);
});

test('runs on different environments are not compared', async ({ page }) => {
	await mockApi(page, 'Prod-like test');
	await page.goto(`${origin}/projects/p/runs/compare?a=${id(1)}&b=${id(2)}`);
	await expect(page.getByRole('alert')).toContainText('Runs use different environments');
	// The subtitle would name only the later run's environment, contradicting the alert.
	await expect(page.getByText('scoring version 1.0')).toHaveCount(0);
	await expect(page.getByRole('region', { name: 'Finding changes' })).toHaveCount(0);
});

test('a run that has not finished is not compared', async ({ page }) => {
	await mockApi(page, 'Staging', 'Running');
	await page.goto(`${origin}/projects/p/runs/compare?a=${id(1)}&b=${id(2)}`);
	await expect(page.getByRole('alert')).toContainText('A selected run has not finished');
	await expect(page.getByRole('region', { name: 'Finding changes' })).toHaveCount(0);
});
