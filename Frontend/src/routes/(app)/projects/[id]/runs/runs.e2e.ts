import { expect, test, type Page } from '@playwright/test';

const origin = 'http://localhost:4173';

function project(id: string) {
	return {
		id, name: `Project ${id.toUpperCase()}`, description: null, defaultEnvironment: null,
		endpointCount: 0, scenarioCount: 0, completedRunCount: 0, failedFindingCount: 0,
		latest: null, spec: null, members: [], findings: [], recentRuns: []
	};
}

function run(projectId: string, number: number) {
	return {
		id: `${projectId}-${number}`, number, environmentName: 'Staging', status: 'Completed',
		completedScenarios: 1, totalScenarios: 1, requestedBy: 'Initial Administrator',
		createdAt: '2026-11-01T00:00:00Z', startedAt: '2026-11-01T00:00:00Z', completedAt: '2026-11-01T00:01:00Z',
		score: 80, previousScore: null, scoringVersion: '1', failureMessage: null, evidenceCoverage: 100
	};
}

// Two projects with 30 runs each, 20 per page, so the pager has a page 2.
async function mockApi(page: Page) {
	await page.route('**/api/auth/me', (route) => route.fulfill({
		json: { id: 'admin-id', userName: 'admin', displayName: 'Initial Administrator', email: null, mustChangePassword: false, roles: ['SuperAdmin'] }
	}));
	await page.route('**/api/projects/overview', (route) => route.fulfill({
		json: {
			projectCount: 2, endpointCount: 0, runsLast7Days: 0, runsPrevious7Days: 0,
			averageScore: null, previousAverageScore: null, scoredRunsLast30Days: 0, failedFindingCount: 0,
			scoreTrend: [], findings: [], recentRuns: [], recentProjects: []
		}
	}));
	await page.route(/\/api\/projects\/(a|b)$/, (route) => route.fulfill({ json: project(route.request().url().slice(-1)) }));
	await page.route(/\/api\/projects\/(a|b)\/runs/, (route) => {
		const url = new URL(route.request().url());
		const id = url.pathname.split('/')[3];
		const pageNo = Number(url.searchParams.get('page') ?? '1');
		const runs = Array.from({ length: 20 }, (_, i) => run(id, 30 - (pageNo - 1) * 20 - i)).filter((r) => r.number > 0);
		return route.fulfill({
			json: { total: 30, matched: 30, page: pageNo, pageSize: 20, environments: ['Staging'], scoringVersions: ['1'], runs }
		});
	});
}

// Client-side navigation, as a direct link inside the app would do (SvelteKit intercepts any same-origin anchor).
async function clientNavigate(page: Page, path: string) {
	await page.evaluate((href) => {
		const a = document.createElement('a');
		a.href = href;
		document.body.append(a);
		a.click();
		a.remove();
	}, path);
	await expect(page).toHaveURL(`${origin}${path}`);
}

const search = (page: Page) => page.getByRole('searchbox', { name: 'Search runs' });

test.beforeEach(async ({ page }) => mockApi(page));

test('a pending search does not follow you to the next page', async ({ page }) => {
	await page.goto(`${origin}/projects/a/runs`);
	await search(page).fill('abc');
	await page.getByRole('navigation', { name: 'Project sections' }).getByRole('link', { name: 'Overview' }).click();
	await expect(page).toHaveURL(`${origin}/projects/a`);
	await page.waitForTimeout(500);
	await expect(page).toHaveURL(`${origin}/projects/a`);
});

test('selection and search text do not carry over to another project', async ({ page }) => {
	await page.goto(`${origin}/projects/a/runs?query=RUN-030`);
	await page.getByRole('checkbox', { name: 'Select RUN-030' }).check();
	await expect(page.getByText('1 run selected')).toBeVisible();
	await expect(search(page)).toHaveValue('RUN-030');

	await clientNavigate(page, '/projects/b/runs');
	await expect(page.getByRole('heading', { level: 1, name: 'Project B' })).toBeVisible();
	await expect(page.getByRole('checkbox', { name: 'Select RUN-030' })).not.toBeChecked();
	await expect(page.getByText(/runs? selected/)).toHaveCount(0);
	await expect(search(page)).toHaveValue('');

	await page.goBack();
	await expect(page).toHaveURL(`${origin}/projects/a/runs?query=RUN-030`);
	await expect(search(page)).toHaveValue('RUN-030');
});

test('Back within a project refills the search box from the URL', async ({ page }) => {
	await page.goto(`${origin}/projects/a/runs`);
	await search(page).fill('foo');
	await expect(page).toHaveURL(`${origin}/projects/a/runs?query=foo`);
	await page.getByRole('link', { name: '2', exact: true }).click();
	await expect(page).toHaveURL(`${origin}/projects/a/runs?query=foo&page=2`);
	await search(page).fill('bar');
	await expect(page).toHaveURL(`${origin}/projects/a/runs?query=bar`);

	await page.goBack();
	await expect(page).toHaveURL(`${origin}/projects/a/runs?query=foo`);
	await expect(search(page)).toHaveValue('foo');
});
