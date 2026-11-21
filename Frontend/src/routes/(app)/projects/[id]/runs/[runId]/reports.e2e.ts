import { expect, test, type Page } from '@playwright/test';

const origin = 'http://localhost:4173';

const completedRun = {
	id: 'r30', number: 30, status: 'Completed', environmentName: 'Staging', requestedBy: 'Maya Chen',
	createdAt: '2026-11-08T08:12:00Z', startedAt: '2026-11-08T08:12:00Z', completedAt: '2026-11-08T08:20:00Z',
	completedScenarios: 2, totalScenarios: 2, skippedScenarios: 0, score: 80, evidenceCoverage: 100, categories: [],
	endpoints: [], scoringVersion: '1.0', failureMessage: null, previous: null, timelines: [], live: null
};

const report = (id: string, version: number) => ({
	id, version, fileName: `Payment-Gateway-RUN-030-report-v${version}.pdf`, generatedAt: '2026-11-08T09:00:00Z', generatedBy: 'Maya Chen'
});

// Project "a" with run r30 finished; `role` is the signed-in user's role, `reports` the run's stored reports.
async function mockRun(page: Page, role: 'Owner' | 'Viewer', reports: ReturnType<typeof report>[]) {
	const state = { generated: 0, reports };
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
			completedRunCount: 1, failedFindingCount: 0, latest: null, spec: null, findings: [], recentRuns: [],
			members: [{ userId: 'maya', displayName: 'Maya Chen', role }]
		}
	}));
	await page.route(/\/api\/projects\/a\/findings/, (route) => route.fulfill({ status: 404 }));
	await page.route(/\/api\/projects\/a\/runs\/r30$/, (route) => route.fulfill({ json: completedRun }));
	await page.route(/\/api\/projects\/a\/runs\/r30\/reports$/, (route) => {
		if (route.request().method() !== 'POST') return route.fulfill({ json: state.reports });
		state.generated++;
		const created = report(`rep${state.reports.length + 1}`, state.reports.length + 1);
		state.reports = [created, ...state.reports];
		return route.fulfill({ status: 201, json: created });
	});
	await page.route(/\/api\/projects\/a\/runs\/r30\/reports\/[^/]+\/file$/, (route) => route.fulfill({
		body: '%PDF-1.7', headers: { 'Content-Type': 'application/pdf', 'Content-Disposition': 'attachment; filename="report.pdf"' }
	}));
	return state;
}

test('an owner generates a report, downloads it, and sees it listed', async ({ page }) => {
	const state = await mockRun(page, 'Owner', []);
	await page.goto(`${origin}/projects/a/runs/r30`);

	const reports = page.getByRole('region', { name: 'PDF reports' });
	const download = page.waitForEvent('download');
	await reports.getByRole('button', { name: 'Generate PDF report' }).click();
	await download;

	expect(state.generated).toBe(1);
	await expect(reports.getByText(/^Version 1 ·/)).toBeVisible();
	await expect(reports.getByRole('link', { name: 'Download version 1' })).toHaveAttribute('href', '/api/projects/a/runs/r30/reports/rep1/file');
	await expect(reports.getByRole('button', { name: 'Generate new version' })).toBeVisible();
});

test('a failed download keeps the results page open', async ({ page }) => {
	await mockRun(page, 'Owner', []);
	await page.route(/\/api\/projects\/a\/runs\/r30\/reports\/[^/]+\/file$/, (route) => route.fulfill({ status: 404 }));
	await page.goto(`${origin}/projects/a/runs/r30`);

	const reports = page.getByRole('region', { name: 'PDF reports' });
	await reports.getByRole('button', { name: 'Generate PDF report' }).click();

	await expect(reports.getByText(/^Version 1 ·/)).toBeVisible();
	await expect(page).toHaveURL(`${origin}/projects/a/runs/r30`);
});

test('a generated report whose answer cannot be read points to the list', async ({ page }) => {
	const state = await mockRun(page, 'Owner', []);
	await page.route(/\/api\/projects\/a\/runs\/r30\/reports$/, (route) => {
		if (route.request().method() !== 'POST') return route.fallback();
		state.reports = [report('rep1', 1)];
		return route.fulfill({ status: 201, body: 'not json' });
	});
	await page.goto(`${origin}/projects/a/runs/r30`);

	const reports = page.getByRole('region', { name: 'PDF reports' });
	await reports.getByRole('button', { name: 'Generate PDF report' }).click();

	await expect(reports.getByRole('alert')).toHaveText('The report was generated. Download it from the list below.');
	await expect(reports.getByText(/^Version 1 ·/)).toBeVisible();
});

test('a viewer downloads existing reports but cannot generate one', async ({ page }) => {
	await mockRun(page, 'Viewer', [report('rep1', 1)]);
	await page.goto(`${origin}/projects/a/runs/r30`);

	const reports = page.getByRole('region', { name: 'PDF reports' });
	await expect(reports.getByRole('link', { name: 'Download version 1' })).toBeVisible();
	await expect(page.getByRole('button', { name: /Generate/ })).toHaveCount(0);
});
