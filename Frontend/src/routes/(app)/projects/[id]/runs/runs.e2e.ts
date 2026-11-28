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
		completedScenarios: 1, totalScenarios: 1, failedScenarios: 0, requestedBy: 'Initial Administrator',
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
		if (url.pathname.endsWith('/reports')) return route.fulfill({ json: [] });
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

test('a run opens its results with the stored scores', async ({ page }) => {
	const finding = {
		id: 'f1', severity: 'Critical', outcome: 'Failed', pattern: 'Idempotency', httpMethod: 'POST', path: '/payments',
		scenarioType: 'DuplicateRequest', summary: 'Duplicate charge after a timeout retry', evidence: {},
		recommendation: { title: 'Persist idempotency keys', text: 'Store the key server-side.' }
	};
	const runUrls: string[] = [];
	const linkedId = '0b5f9c4e-2d7a-4e1b-9c3f-6a8d2e4f1b7c';
	await page.route(/\/api\/projects\/a\/runs\/a-30(\?|$)/, (route) => (runUrls.push(route.request().url()), route.fulfill({
		json: {
			...run('a', 30), skippedScenarios: 2, score: 78, previous: { id: 'a-29', number: 29, score: 70 }, evidenceCoverage: 66.67,
			categories: [{ category: 'Idempotency', weight: 10, score: 50, findings: 2, inconclusive: 0 }],
			endpoints: [{ httpMethod: 'POST', path: '/payments', score: 50 }],
			problems: [{ status: 'Skipped', reason: 'The dependency simulator is not set up.', count: 2 }],
			timelines: [{
				executionId: 'e1', httpMethod: 'POST', path: '/payments', scenarioType: 'DuplicateRequest', outcome: 'Failed',
				failureReason: 'record count ≤ 1 · actual 2', completedAt: '2026-11-01T00:00:06Z',
				events: [{ attemptNo: 1, requestMethod: 'POST', requestPath: '/payments', startedAt: '2026-11-01T00:00:02Z', durationMs: 140,
					clientStatusCode: 201, upstreamStatusCode: 201, outcomeType: 'Forwarded', errorType: null, phase: 'fault' }]
			}, {
				executionId: linkedId, httpMethod: 'GET', path: '/payments', scenarioType: 'Throttle429', outcome: 'Degraded',
				failureReason: null, completedAt: null, events: []
			}]
		}
	})));
	await page.route(/\/api\/projects\/a\/findings\?run=a-30$/, (route) => route.fulfill({
		json: {
			runs: [], run: null, findings: [
				finding, { ...finding, id: 'f2', outcome: 'Passed', severity: 'Info' },
				{ ...finding, id: 'f3', outcome: 'Inconclusive', severity: 'Info', recommendation: null }
			]
		}
	}));

	await page.goto(`${origin}/projects/a/runs`);
	await page.getByRole('link', { name: 'RUN-030' }).click();
	await expect(page).toHaveURL(`${origin}/projects/a/runs/a-30`);
	await expect(page.getByRole('heading', { name: 'RUN-030 results' })).toBeVisible();
	await expect(page.getByRole('navigation', { name: 'Project sections' }).getByRole('link', { name: 'Test runs' })).toHaveAttribute('aria-current', 'page');
	await expect(page.getByText('+8 vs RUN-029')).toBeVisible();
	await expect(page.getByRole('row', { name: /Idempotency 10% 50 2 findings/ })).toBeVisible();
	await expect(page.getByText('67%')).toBeVisible();
	await expect(page.getByText('2 of 3 findings scored.')).toBeVisible();
	await expect(page.getByText('Persist idempotency keys')).toBeVisible();
	await expect(page.getByText('No runner errors in this run. 2 scenarios were skipped.')).toBeVisible();
	await expect(page.getByRole('listitem').filter({ hasText: '2 skipped: The dependency simulator is not set up.' })).toBeVisible();
	await expect(page.getByText('record count ≤ 1 · actual 2')).toBeVisible();
	await expect(page.locator('#timeline').getByText('Fault phase starts')).toBeVisible();

	// A finding's "View in timeline" link opens its scenario and asks the API for it.
	await page.goto(`${origin}/projects/a/runs/a-30?timeline=${linkedId}#timeline`);
	await expect(page.getByRole('combobox', { name: 'Scenario' })).toHaveValue(linkedId);
	await expect(page.getByText('No requests were recorded for this scenario.')).toBeVisible();
	expect(runUrls.at(-1)).toMatch(new RegExp(`\\?timeline=${linkedId}$`));

	// A malformed id is not sent, so the API can't refuse it and the run still opens on its worst timeline.
	await page.goto(`${origin}/projects/a/runs/a-30?timeline=junk`);
	await expect(page.getByRole('heading', { name: 'RUN-030 results' })).toBeVisible();
	await expect(page.getByRole('combobox', { name: 'Scenario' })).toHaveValue('e1');
	expect(runUrls.at(-1)).toMatch(/\/runs\/a-30$/);
	await page.getByRole('combobox', { name: 'Scenario' }).selectOption(linkedId);
	await expect(page.getByText('No requests were recorded for this scenario.')).toBeVisible();
});

test('a run whose scenarios failed before sending a request lists why', async ({ page }) => {
	const reason = 'The required header parameter "Idempotency-Key" has no example, default or enum value in the spec.';
	await page.route(/\/api\/projects\/a\/runs\/a-30$/, (route) => route.fulfill({
		json: {
			...run('a', 30), status: 'PartiallyCompleted', completedScenarios: 26, totalScenarios: 26, skippedScenarios: 0,
			score: null, previous: null, evidenceCoverage: null, categories: null, endpoints: [], timelines: [],
			problems: [{ status: 'Failed', reason, count: 17 }]
		}
	}));
	await page.route(/\/api\/projects\/a\/findings\?run=a-30$/, (route) => route.fulfill({ json: { runs: [], run: null, findings: [] } }));

	await page.goto(`${origin}/projects/a/runs/a-30`);
	await expect(page.getByText('17 scenarios failed.')).toBeVisible();
	await expect(page.getByRole('listitem').filter({ hasText: `17 failed: ${reason}` })).toBeVisible();
	await expect(page.getByText(/No runner errors/)).toHaveCount(0);
});

test('a completed run with nothing scored says so instead of claiming it did not finish', async ({ page }) => {
	await page.route(/\/api\/projects\/a\/runs\/a-30$/, (route) => route.fulfill({
		json: {
			...run('a', 30), skippedScenarios: 0, score: null, previous: null, evidenceCoverage: 0,
			categories: [{ category: 'Idempotency', weight: 10, score: null, findings: 1, inconclusive: 1 }], endpoints: [], timelines: [], problems: []
		}
	}));
	await page.route(/\/api\/projects\/a\/findings\?run=a-30$/, (route) => route.fulfill({ json: { runs: [], run: null, findings: [] } }));

	await page.goto(`${origin}/projects/a/runs`);
	await page.getByRole('link', { name: 'RUN-030' }).click();
	await expect(page.getByText('No scored findings in this run.')).toBeVisible();
	await expect(page.getByText(/every scenario ran/)).toHaveCount(0);
});
