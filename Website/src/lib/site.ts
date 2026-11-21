export const repoUrl = 'https://github.com/Neil-urk12/ChaosLab';

// Sidebar order of the docs pages (FeaturesFlow 2.0).
export const docsPages = [
	{ href: '/docs/installation', title: 'Installation' },
	{ href: '/docs/first-run-setup', title: 'First-run setup' },
	{ href: '/docs/configuration', title: 'Configuration' },
	{ href: '/docs/upgrading', title: 'Upgrading' },
	{ href: '/docs/safe-use', title: 'Safe use' }
] as const;

// Shown in the hero and on the Installation page until the release image exists (FeaturesFlow 2.0).
// Two blocks, so a pasted copy cannot start the pod before the secrets are edited: Postgres keeps the
// password it was first created with. cp -n keeps an existing .env.local and its credentials key.
export const fromSourceSetup = `git clone ${repoUrl}.git && cd ChaosLab
make secret-init && cp -n Backend/.env.example Backend/.env.local`;
export const fromSourceStart = `make build && make up && make migrate && make restart
(cd Frontend && pnpm install) && make start`;
