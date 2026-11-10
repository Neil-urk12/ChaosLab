import { expect, test, type Page } from '@playwright/test';

const origin = 'http://localhost:4173';

const member = (userId: string, displayName: string, role: string, removedAt: string | null = null) => ({
	userId, displayName, email: `${displayName.split(' ')[0].toLowerCase()}@chaoslab.test`, role, joinedAt: '2026-09-12T08:00:00Z', removedAt
});

// Signed in as Maya, an owner of project "a" alongside a second owner, Alex.
async function mockTeam(page: Page) {
	const posted: unknown[] = [];
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
			completedRunCount: 0, failedFindingCount: 0, latest: null, spec: null, members: [], findings: [], recentRuns: []
		}
	}));
	await page.route(/\/api\/projects\/a\/members\/candidates/, (route) => route.fulfill({
		json: [
			{ userId: 'priya', displayName: 'Priya Nair', email: 'priya@chaoslab.test', role: null },
			{ userId: 'maya', displayName: 'Maya Chen', email: 'maya@chaoslab.test', role: 'Owner' }
		]
	}));
	await page.route(/\/api\/projects\/a\/members$/, (route) => {
		if (route.request().method() === 'POST') {
			posted.push(route.request().postDataJSON());
			return route.fulfill({ status: 204 });
		}
		return route.fulfill({
			json: {
				yourRole: 'Owner',
				members: [member('maya', 'Maya Chen', 'Owner'), member('alex', 'Alex Morgan', 'Owner')],
				removed: [member('lee', 'Lee Park', 'Editor', '2026-09-28T08:00:00Z')]
			}
		});
	});
	return posted;
}

test('owner searches, picks and adds a member, and can re-add a removed one', async ({ page }) => {
	const posted = await mockTeam(page);
	await page.goto(`${origin}/projects/a/team`);
	await expect(page.getByText('2 active members')).toBeVisible();
	await expect(page.getByText('was Editor')).toBeVisible();

	await page.getByRole('searchbox', { name: 'Search members' }).fill('alex');
	await expect(page.locator('tbody tr')).toHaveCount(1);

	await page.getByRole('button', { name: 'Add member' }).click();
	const panel = page.getByRole('complementary', { name: 'Add member' });
	await panel.getByRole('searchbox').fill('pri');
	await expect(panel.getByRole('option', { name: /Maya Chen/ }).getByRole('button')).toBeDisabled();
	await panel.getByRole('button', { name: /Priya Nair/ }).click();
	await panel.getByText('Viewer', { exact: true }).first().click();
	await panel.getByRole('button', { name: 'Add Priya Nair' }).click();
	await expect(panel).toHaveCount(0);

	await page.getByRole('button', { name: 'Re-add' }).click();
	await expect.poll(() => posted).toEqual([{ userId: 'priya', role: 'Viewer' }, { userId: 'lee', role: 'Editor' }]);
});

test('owner who removes themselves goes back to the project list', async ({ page }) => {
	await mockTeam(page);
	await page.route(/\/api\/projects\/a\/members\/maya$/, (route) => route.fulfill({ status: 204 }));
	await page.route(/\/api\/projects$/, (route) => route.fulfill({ json: [] }));

	await page.goto(`${origin}/projects/a/team`);
	await page.getByRole('button', { name: 'Actions for Maya Chen' }).click();
	page.on('dialog', (dialog) => dialog.accept());
	await page.getByRole('menuitem', { name: 'Remove from project' }).click();
	await expect(page).toHaveURL(`${origin}/projects`);
});

test('dismissing the leave prompt keeps the owner on the team page', async ({ page }) => {
	await mockTeam(page);
	const calls: string[] = [];
	await page.route(/\/api\/projects\/a\/members\/maya$/, (route) => {
		calls.push(route.request().method());
		return route.fulfill({ status: 204 });
	});

	await page.goto(`${origin}/projects/a/team`);
	let prompt = '';
	page.on('dialog', (dialog) => {
		prompt = dialog.message();
		return dialog.dismiss();
	});
	await page.getByRole('button', { name: 'Actions for Maya Chen' }).click();
	await page.getByRole('menuitem', { name: 'Remove from project' }).click();

	expect(prompt).toBe('Leave Payment Gateway? Another owner will have to add you back.');
	await expect(page).toHaveURL(`${origin}/projects/a/team`);
	expect(calls).toEqual([]);
});

test('a failed role change puts the select back, and Escape closes the actions menu', async ({ page }) => {
	await mockTeam(page);
	await page.route(/\/api\/projects\/a\/members\/alex$/, (route) => route.fulfill({ status: 409, json: 'A project needs at least one owner.' }));

	await page.goto(`${origin}/projects/a/team`);
	const select = page.getByLabel('Role of Alex Morgan');
	await select.selectOption('Editor');
	await expect(page.getByRole('alert')).toBeVisible();
	await expect(select).toHaveValue('Owner');

	await page.getByRole('button', { name: 'Actions for Alex Morgan' }).click();
	await expect(page.getByRole('menu')).toBeVisible();
	await page.keyboard.press('Escape');
	await expect(page.getByRole('menu')).toHaveCount(0);
});
