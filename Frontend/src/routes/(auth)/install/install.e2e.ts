import { expect, test, type Page } from '@playwright/test';

const token = 'A'.repeat(64);
const setupUrl = `http://localhost:4173/install#token=${token}`;

async function fillAccount(page: Page) {
	await page.getByLabel('First name', { exact: true }).fill('Initial');
	await page.getByLabel('Last name', { exact: true }).fill('Administrator');
	await page.getByLabel('Username', { exact: true }).fill('admin');
	await page.getByLabel('Email', { exact: true }).fill('admin@example.test');
	await page.getByLabel('Password', { exact: true }).fill('Aa1!test-password');
	await page.getByLabel('Confirm password', { exact: true }).fill('Aa1!test-password');
}

test('missing setup link does not offer account creation', async ({ page }) => {
	await page.goto('http://localhost:4173/install');
	await expect(page.getByRole('alert')).toContainText('complete setup link');
	await expect(page.getByRole('button', { name: 'Create SuperAdmin & sign in' })).toHaveCount(0);
});

test('password mismatch does not submit', async ({ page }) => {
	let requests = 0;
	await page.route('**/api/auth/setup', async (route) => {
		requests++;
		await route.fulfill({ status: 204 });
	});
	await page.goto(setupUrl);
	await fillAccount(page);
	await page.getByLabel('Confirm password', { exact: true }).fill('different');
	await page.getByRole('button', { name: 'Create SuperAdmin & sign in' }).click();
	await expect(page.getByRole('alert')).toContainText("Passwords don't match");
	expect(requests).toBe(0);
});

test('submits token in body, signs in and replaces the setup URL', async ({ page }) => {
	await page.route('**/api/auth/me', (route) => route.fulfill({ json: {
		id: 'admin-id', userName: 'admin', displayName: 'Initial Administrator',
		email: 'admin@example.test', mustChangePassword: false, roles: ['SuperAdmin']
	} }));
	await page.route('**/api/projects', (route) => route.fulfill({ json: [] }));
	await page.route('**/api/auth/setup', (route) => route.fulfill({ status: 204 }));
	await page.goto(setupUrl);
	await fillAccount(page);
	const submitted = page.waitForRequest('**/api/auth/setup');
	await page.getByRole('button', { name: 'Create SuperAdmin & sign in' }).click();
	const request = await submitted;
	expect(request.method()).toBe('POST');
	expect(request.url()).not.toContain(token);
	expect(request.headers()['referer']).toBeUndefined();
	expect(request.postDataJSON()).toEqual({
		token, username: 'admin', email: 'admin@example.test', firstName: 'Initial',
		lastName: 'Administrator', password: 'Aa1!test-password'
	});
	await expect(page).toHaveURL('http://localhost:4173/');
	await expect(page.getByRole('heading', { level: 1, name: /^Good (morning|afternoon|evening), Initial$/ })).toBeVisible();
});

for (const [status, message] of [[403, 'invalid or expired'], [409, 'already set up']] as const) {
	test(`setup ${status} explains recovery and prevents resubmission`, async ({ page }) => {
		await page.route('**/api/auth/setup', (route) => route.fulfill({ status }));
		await page.goto(setupUrl);
		await fillAccount(page);
		await page.getByRole('button', { name: 'Create SuperAdmin & sign in' }).click();
		await expect(page.getByRole('alert')).toContainText(message);
		await expect(page.getByRole('button', { name: 'Create SuperAdmin & sign in' })).toHaveCount(0);
		await expect(page.getByRole('link', { name: 'Sign in', exact: true })).toBeVisible();
	});
}

test('server validation preserves form for correction', async ({ page }) => {
	await page.route('**/api/auth/setup', (route) => route.fulfill({
		status: 400, json: { errors: { password: ['Choose a stronger password.'] } }
	}));
	await page.goto(setupUrl);
	await fillAccount(page);
	await page.getByRole('button', { name: 'Create SuperAdmin & sign in' }).click();
	await expect(page.getByRole('alert')).toContainText('Choose a stronger password.');
	await expect(page.getByRole('button', { name: 'Create SuperAdmin & sign in' })).toBeEnabled();
	await expect(page.getByLabel('Username', { exact: true })).toHaveValue('admin');
});


test('identical field errors render without a duplicate-key crash', async ({ page }) => {
	const pageErrors: string[] = [];
	page.on('pageerror', (error) => pageErrors.push(error.message));
	await page.route('**/api/auth/setup', (route) => route.fulfill({
		status: 400, json: { errors: {
			firstName: ['This field is required.'], lastName: ['This field is required.']
		} }
	}));
	await page.goto(setupUrl);
	await fillAccount(page);
	await page.getByRole('button', { name: 'Create SuperAdmin & sign in' }).click();
	await expect(page.getByRole('alert').getByRole('listitem')).toHaveCount(2);
	await expect(page.getByRole('button', { name: 'Create SuperAdmin & sign in' })).toBeEnabled();
	expect(pageErrors).toEqual([]);
});
