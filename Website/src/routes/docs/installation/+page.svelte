<script lang="ts">
	import { resolve } from '$app/paths';
	import CodeBlock from '$lib/CodeBlock.svelte';
	import { fromSourceSetup, fromSourceStart, repoUrl } from '$lib/site';
</script>

<h1>Installation</h1>

<p>
	There will be two ways to install a ChaosLab instance: from the release image, and from source.
	The release image, with a one-command install, arrives with v0.1.0. Until then, install from
	source.
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

<h2>From the release image</h2>
<p>
	Coming with v0.1.0: one image with the backend and the web app, a <code>compose.yaml</code> that runs
	it with PostgreSQL under Docker or Podman, and an install script that generates the secrets for you.
	No .NET SDK or Node.js needed.
</p>
