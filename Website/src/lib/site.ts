export const repoUrl = 'https://github.com/Neil-urk12/ChaosLab';

// Sidebar order of the docs pages (FeaturesFlow 2.0).
export const docsPages = [
	{ href: '/docs/installation', title: 'Installation' },
	{ href: '/docs/first-run-setup', title: 'First-run setup' },
	{ href: '/docs/configuration', title: 'Configuration' },
	{ href: '/docs/upgrading', title: 'Upgrading' },
	{ href: '/docs/safe-use', title: 'Safe use' }
] as const;

// The release whose compose.yaml the install commands download. Bump it with each release tag.
export const release = 'v0.2.0';
const composeUrl = `https://raw.githubusercontent.com/Neil-urk12/ChaosLab/${release}/compose.yaml`;

// The image install, in the hero and on the Installation page (FeaturesFlow 2.0). One && chain: mkdir
// fails on an existing chaoslab/ folder, so a pasted copy never replaces a kept .env and its credentials key.
// hash comes first and names a missing tool: without openssl, $(openssl …) is empty and .env would get empty
// secrets; without curl, an empty chaoslab/ folder would block the next try.
export const imageInstall = `hash curl openssl && mkdir chaoslab && cd chaoslab &&
curl -fsSLO ${composeUrl} &&
printf 'CREDENTIALS_KEY=%s\\nPOSTGRES_PASSWORD=%s\\n' "$(openssl rand -base64 32)" "$(openssl rand -hex 24)" > .env &&
docker compose up -d && docker compose logs -f chaoslab`;

// The same for Windows PowerShell 5.1 and PowerShell 7. The script block pastes as one input, and Stop ends it at
// the first error, so an existing chaoslab/ folder stops it before .env is written.
export const imageInstallPowerShell = `& {
  $ErrorActionPreference = 'Stop'
  New-Item -ItemType Directory chaoslab | Out-Null; Set-Location chaoslab
  Invoke-WebRequest ${composeUrl} -OutFile compose.yaml -UseBasicParsing
  $b = [byte[]]::new(32); [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
  "CREDENTIALS_KEY=$([Convert]::ToBase64String($b))\`nPOSTGRES_PASSWORD=$((New-Guid).ToString('N'))" | Set-Content .env
  docker compose up -d; docker compose logs -f chaoslab
}`;

// The from-source install, on the Installation page (FeaturesFlow 2.0).
// Two blocks, so a pasted copy cannot start the pod before the secrets are edited: Postgres keeps the
// password it was first created with. cp -n keeps an existing .env.local and its credentials key.
export const fromSourceSetup = `git clone ${repoUrl}.git && cd ChaosLab
make secret-init && cp -n Backend/.env.example Backend/.env.local`;
export const fromSourceStart = `make build && make up && make migrate && make restart
(cd Frontend && pnpm install) && make start`;
