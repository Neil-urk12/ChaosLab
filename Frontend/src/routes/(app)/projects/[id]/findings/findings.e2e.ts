import { expect, test, type Page } from '@playwright/test';

const origin = 'http://localhost:4173';

const finding = (id: string, severity: string, outcome: string, httpMethod: string, path: string, scenarioType: string, summary: string, evidence = {}) => ({
	id, severity, outcome, pattern: 'Retry', httpMethod, path, scenarioType, summary, evidence, recommendation: null
});

const findings = [
	{
		...finding('f1', 'Critical', 'Failed', 'POST', '/payments', 'DuplicateRequest', 'Duplicate charge after a timeout retry', {
			detail: 'Duplicate request + Timeout',
			expected: 'One payment for the same Idempotency-Key, even when the first attempt times out.',
			observed: 'Two payments were created. The first attempt timed out after 3.0 s; the second returned 201.',
			invariantViolated: true,
			facts: [{ label: 'Status codes', value: '504 → 201' }, { label: 'Correlation ID', value: 'corr-91c2' }],
			interpretation: { observed: 'Two records exist for one logical operation.', suggests: 'The endpoint does not persist or check the Idempotency-Key.', notProven: 'Which layer retried.' }
		}),
		recommendation: { title: 'Persist keys', text: 'Persist the Idempotency-Key server-side.' }
	},
	finding('f2', 'High', 'Degraded', 'GET', '/payments', 'Throttle429', 'Retry-After ignored on 1 of 3 throttles'),
	finding('f3', 'Low', 'Inconclusive', 'GET', '/health', 'SlowDrip', 'No observable complete-operation deadline'),
	finding('f4', 'Info', 'Passed', 'GET', '/refunds/{id}', 'Latency', 'Latency handled')
];

async function mockApi(page: Page) {
	await page.route('**/api/auth/me', (route) => route.fulfill({
		json: { id: 'admin-id', userName: 'admin', displayName: 'Initial Administrator', email: null, mustChangePassword: false, roles: ['SuperAdmin'] }
	}));
	await page.route('**/api/projects/overview', (route) => route.fulfill({
		json: {
			projectCount: 1, endpointCount: 0, runsLast7Days: 0, runsPrevious7Days: 0, averageScore: null, previousAverageScore: null,
			scoredRunsLast30Days: 0, failedFindingCount: 1, scoreTrend: [], findings: [], recentRuns: [], recentProjects: []
		}
	}));
	await page.route(/\/api\/projects\/a$/, (route) => route.fulfill({
		json: {
			id: 'a', name: 'Payment Gateway', description: null, defaultEnvironment: null, endpointCount: 0, scenarioCount: 0,
			completedRunCount: 1, failedFindingCount: 1, latest: null, spec: null, members: [], findings: [], recentRuns: []
		}
	}));
	await page.route(/\/api\/projects\/a\/findings/, (route) => {
		const run = { id: 'r26', number: 26, status: 'Completed', completedAt: '2026-10-04T06:32:00Z' };
		return route.fulfill({ json: { runs: [run], run, findings } });
	});
}

test.beforeEach(async ({ page }) => mockApi(page));

test('findings default to Fail and Degraded and show the first one in detail', async ({ page }) => {
	await page.goto(`${origin}/projects/a/findings`);
	await expect(page.getByText('From RUN-026')).toContainText('3 of 4 findings scored');

	const rows = page.locator('tbody tr');
	await expect(rows).toHaveCount(2);
	const details = page.getByRole('complementary', { name: 'Finding details' });
	await expect(details.getByRole('heading', { name: 'Duplicate charge after a timeout retry' })).toBeVisible();
	await expect(details.getByText('Invariant violated')).toBeVisible();
	await expect(details.getByText('504 → 201')).toBeVisible();
	await expect(details.getByText('Persist the Idempotency-Key server-side.')).toBeVisible();

	await page.getByRole('button', { name: 'GET /payments' }).click();
	await expect(details.getByRole('heading', { name: 'Retry-After ignored on 1 of 3 throttles' })).toBeVisible();

	await page.getByText('Outcome: Fail, Degraded').click();
	await page.getByRole('checkbox', { name: 'Inconclusive' }).check();
	await expect(rows).toHaveCount(3);

	await page.getByRole('searchbox', { name: 'Search findings' }).fill('health');
	await expect(rows).toHaveCount(1);

	await details.getByRole('button', { name: 'Close finding details' }).click();
	await expect(details).toHaveCount(0);
});
