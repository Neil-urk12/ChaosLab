<script lang="ts">
	import { resolve } from '$app/paths';
	import CodeBlock from '$lib/CodeBlock.svelte';
	import {
		fromSourceSetup,
		fromSourceStart,
		imageInstall,
		imageInstallPowerShell,
		release,
		repoUrl
	} from '$lib/site';
</script>

<h1>Installation</h1>

<p>
	There are two ways to install a ChaosLab instance. The release image needs only Docker or Podman.
	Installing from source builds everything yourself, for working on ChaosLab.
</p>

<h2>From the release image</h2>

<h3>Prerequisites</h3>
<ul>
	<li>
		<a href="https://docs.docker.com/compose/install/">Docker with the Compose plugin</a>, or
		<a href="https://podman.io/">Podman</a> with <code>podman compose</code>
	</li>
	<li><code>curl</code> and <code>openssl</code>, or PowerShell on Windows</li>
</ul>

<h3>Quick version</h3>
<CodeBlock code={imageInstall} label="Copy the install commands" />
<p>On Windows, in PowerShell:</p>
<CodeBlock code={imageInstallPowerShell} label="Copy the PowerShell install commands" />
<p>
	With Podman, write <code>podman compose</code> instead of <code>docker compose</code>. Once the setup
	link appears, open it and continue with
	<a href={resolve('/docs/first-run-setup')}>First-run setup</a>. <kbd>Ctrl</kbd>+<kbd>C</kbd> stops
	following the logs and leaves ChaosLab running.
</p>

<h3>Step by step</h3>
<ol>
	<li>
		Create a folder for the instance and download the {release} <code>compose.yaml</code> into it. The
		quick version stops if a <code>chaoslab</code> folder already exists.
	</li>
	<li>
		Create <code>.env</code> next to it, once, with a generated credentials key and database password.
		The header of <code>compose.yaml</code> has the same commands. Keep this file and back it up: a new
		key or password cuts ChaosLab off from its database and from every stored target credential.
	</li>
	<li>
		Start the stack. It pulls the ChaosLab image and PostgreSQL 17, applies the database migrations and
		serves the web app on <code>http://localhost:8080</code>:
		<CodeBlock code="docker compose up -d" />
	</li>
	<li>
		Read the setup link from the logs and continue with
		<a href={resolve('/docs/first-run-setup')}>First-run setup</a>:
		<CodeBlock code="docker compose logs chaoslab" />
	</li>
</ol>

<h3>Browsers and other machines</h3>
<p>
	<code>compose.yaml</code> listens on <code>localhost</code> only. Sign-in cookies are Secure: Chrome,
	Edge and Firefox accept them over plain HTTP on <code>localhost</code>, but Safari does not. To use
	Safari, or to reach ChaosLab from other machines, put an HTTPS reverse proxy in front of port 8080
	and set <code>BootstrapSetup__PublicUrl</code> in <code>compose.yaml</code> to its address (see
	<a href={resolve('/docs/configuration')}>Configuration</a>).
</p>

<h3>Stopping</h3>
<p>
	<code>docker compose down</code> stops ChaosLab and keeps the database, sign-in keys and reports in
	their volumes. <code>docker compose down -v</code> deletes them for good.
</p>

<h2>From source</h2>

<h3>Prerequisites</h3>
<ul>
	<li><a href="https://podman.io/">Podman</a> and <code>envsubst</code> (from the <code>gettext</code> package)</li>
	<li>Make</li>
	<li>
		The <a href="https://dotnet.microsoft.com/download/dotnet/10.0">.NET 10 SDK</a> and
		<code>dotnet-ef</code>: <code>dotnet tool install --global dotnet-ef --version 10.0.12</code>
	</li>
	<li>Node.js 20.19+, 22.12+ or 24+ and <a href="https://pnpm.io/">pnpm</a></li>
	<li><code>openssl</code>, to generate the credentials key</li>
</ul>

<h3>Quick version</h3>
<CodeBlock code={fromSourceSetup} label="Copy the setup commands" />
<p>
	Set the same database password and credentials key in <code>secret.yaml</code> and
	<code>Backend/.env.local</code> before you go on. The database keeps the password it is first
	created with.
</p>
<CodeBlock code={fromSourceStart} label="Copy the build and start commands" />

<h3>Step by step</h3>
<ol>
	<li>
		Clone the repository:
		<CodeBlock code={`git clone ${repoUrl}.git\ncd ChaosLab`} />
	</li>
	<li>
		Create <code>secret.yaml</code> with <code>make secret-init</code>. In it, set
		<code>postgres-password</code>, the same password inside <code>connection-string</code>, and a
		<code>credentials-key</code> from <code>openssl rand -base64 32</code>.
	</li>
	<li>
		Copy <code>Backend/.env.example</code> to <code>Backend/.env.local</code> and set the same database
		password and credentials key there. The local backend and <code>make migrate</code> read this file.
	</li>
	<li>
		Build the backend image and start the pod. It runs PostgreSQL on <code>127.0.0.1:5432</code> and a
		backend container on <code>127.0.0.1:8080</code>:
		<CodeBlock code="make build && make up" />
	</li>
	<li>
		Create the database schema, then restart the pod so its backend starts against it:
		<CodeBlock code="make migrate && make restart" />
	</li>
	<li>
		Install the frontend's packages and start the local backend (<code>:5274</code>) and frontend
		(<code>:5173</code>):
		<CodeBlock code="(cd Frontend && pnpm install) && make start" />
	</li>
	<li>
		Open the setup link printed in the <code>make start</code> terminal and continue with
		<a href={resolve('/docs/first-run-setup')}>First-run setup</a>.
	</li>
</ol>

<h3>Stopping</h3>
<p>
	Press <kbd>Ctrl</kbd>+<kbd>C</kbd> to stop <code>make start</code>, and <code>make down</code> to stop
	the pod. <code>make down</code> keeps the database, sign-in keys and reports in their volumes.
	<code>make clean-data</code> deletes the database and the reports volume for good. Reports generated
	through <code>make start</code> are in <code>Backend/reports/</code>; delete that folder yourself.
</p>
